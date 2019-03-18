﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using System.IO;
using UnityEditor;

class PCNode: MonoBehaviour
{
    #region subtypes

    private enum MeshState
    {
        LOADED, NOT_LOADED
    }

    public enum PCNodeState
    {
        VISIBLE,
        INVISIBLE
    };

    public struct NodeAndDistance : System.IComparable<NodeAndDistance>
    {
        public PCNode node;
        public float estimatedDistanceToCamera;

        public int CompareTo(NodeAndDistance other)
        {
            return other.estimatedDistanceToCamera.CompareTo(estimatedDistanceToCamera);
        }
    }

    #endregion

    #region Attributes

    FileInfo fileInfo = null;
    IPointCloudManager pointCloudManager = null;
    MeshRenderer meshRenderer = null;
    MeshFilter meshFilter = null;
    private MeshState currentMeshState = MeshState.NOT_LOADED;
    float averagePointDistance = 0.0f;

    protected BoundingSphere boundingSphere;
    public Bounds boundsInModelSpace { get; private set; }
    protected PCNode[] children = null;

    private PCNodeState _state = PCNodeState.INVISIBLE;
    public PCNodeState State
    {
        get { return _state; }
        set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged();
            }
        }
    }

    #endregion

    #region JSON Initialization

    public void InitializeFromJSON(JSONNode node)
    {
        Vector3 min = JSONNode2Vector3(node["min"]);
        Vector3 max = JSONNode2Vector3(node["max"]);
        Vector3 center = (min + max) / 2.0f;
        Vector3 size = max - min;
        boundingSphere = new BoundingSphere(center, size.magnitude);

        averagePointDistance = node["avgDistance"];

        boundsInModelSpace = new Bounds(center, size);
    }

    public static Vector3 JSONNode2Vector3(JSONNode node)
    {
        return new Vector3(node[0].AsFloat, node[2].AsFloat, node[1].AsFloat);
    }

    public static PCNode AddNode(JSONNode node, DirectoryInfo directory, GameObject gameObject, IPointCloudManager materialProvider)
    {
        GameObject child = new GameObject("PC Node");
        child.isStatic = true;

        PCNode pcNode = child.AddComponent<PCNode>();
        pcNode.Initialize(node, directory, materialProvider);
        child.transform.SetParent(gameObject.transform, false);
        return pcNode;
    }

    public bool Initialize(JSONNode node, DirectoryInfo directory, IPointCloudManager manager)
    {
        gameObject.name = "PCNode";
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshFilter.mesh = null;
        meshRenderer = gameObject.AddComponent<MeshRenderer>();

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;

        this.pointCloudManager = manager;
        string filename = node["filename"];
        fileInfo = directory.GetFiles(filename)[0];
        Debug.Assert(this.fileInfo != null, "File not found:" + node["filename"]);
        Debug.Assert(this.pointCloudManager != null, "No PCManager");

        gameObject.name = "PC Parent Node";

        JSONArray childrenJSON = node["children"].AsArray;
        ArrayList childrenList = new ArrayList();
        //Debug.Log("N Children: " + childrenJSON.Count);
        for (int i = 0; i < childrenJSON.Count; i++)
        {
            PCNode pcNode = PCNode.AddNode(childrenJSON[i], directory, gameObject, manager);
            if (pcNode != null)
            {
                childrenList.Add(pcNode);
            }
        }
        children = (PCNode[])childrenList.ToArray(typeof(PCNode));

        InitializeFromJSON(node);

        return childrenJSON.Count > 0;
    }

    #endregion

    #region LOD

    public float EstimatedDistance(Vector3 position)
    {
        return boundsInModelSpace.MinDistance(position);
    }

    void FetchMesh()
    {
        if (currentMeshState == MeshState.NOT_LOADED)
        {
            float dist = boundingSphere.DistanceTo(Camera.main.transform.position);
            float priority = Camera.main.farClipPlane - dist;

            Mesh mesh = pointCloudManager.GetMeshManager().CreateMesh(fileInfo, pointCloudManager, priority);
            if (mesh != null)
            {
                meshFilter.mesh = mesh;
                currentMeshState = MeshState.LOADED;
            }
        }
    }

    private void RemoveMesh()
    {
        if (currentMeshState == MeshState.LOADED)
        {
            pointCloudManager.GetMeshManager().ReleaseMesh(meshFilter.mesh); //Returning Mesh
            meshFilter.mesh = null;
            currentMeshState = MeshState.NOT_LOADED;
        }
    }

    //// Update is called once per frame
    //void Update()
    //{
    //    if (State == PCNodeState.VISIBLE)
    //    {
    //        if (currentMeshState == MeshState.NOT_LOADED)
    //        {
    //            FetchMesh();
    //        }
    //        pointCloudManager.ModifyRendererBasedOnBounds(boundsInModelSpace, meshRenderer);
    //    }
    //    else
    //    {
    //        if (currentMeshState == MeshState.LOADED)
    //        {
    //            RemoveMesh();
    //        }
    //    }
    //}

    // Update is called once per frame
    public void CheckMeshState()
    {
        if (State == PCNodeState.VISIBLE)
        {
            FetchMesh();
            pointCloudManager.ModifyRendererBasedOnBounds(boundsInModelSpace, meshRenderer);
        }

        foreach (PCNode node in children)
        {
            node.CheckMeshState();
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = (State == PCNodeState.VISIBLE) ? Color.red : Color.blue;

        if (meshRenderer.materials.Length > 1)
        {
            Gizmos.color = Color.green;
        }

        Bounds b = boundsInModelSpace;
        Gizmos.DrawWireCube(b.center, b.size);

        //Gizmos.DrawWireSphere(boundingSphere.position, boundingSphere.radius);
    }

    public void ComputeNodeState(ref List<PCNode.NodeAndDistance> visibleLeafNodesAndDistances,
                                        Vector3 camPosition, 
                                        float zFar)
    {
        float dist = EstimatedDistance(camPosition);
        State = (dist <= zFar && dist <= averagePointDistance) ? PCNodeState.VISIBLE : PCNodeState.INVISIBLE;
        if (State == PCNodeState.VISIBLE)
        {
            NodeAndDistance nodeAndDistance = new NodeAndDistance();
            nodeAndDistance.node = this;
            nodeAndDistance.estimatedDistanceToCamera = dist;
            visibleLeafNodesAndDistances.Add(nodeAndDistance);

            foreach (PCNode node in children)
            {
                node.ComputeNodeState(ref visibleLeafNodesAndDistances, camPosition, zFar);
                node.gameObject.SetActive(node.State == PCNodeState.VISIBLE);
                if (node.State == PCNodeState.INVISIBLE)
                {
                    node.RemoveMesh();
                }
            }
        }
        else
        {
            //Debug.Log("PCNode not visible.");
        }
    }


    public void OnStateChanged()
    {
        //switch (State)
        //{
        //    case PCNodeState.INVISIBLE:
        //        Debug.Log("Leaf Node set invisible.");
        //        break;
        //    case PCNodeState.VISIBLE:
        //        Debug.Log("Leaf Node set invisible.");
        //        break;
        //}
    }

    #endregion

    #region Pointpicking

    public void GetClosestPointOnRay(Ray ray,
                                                    Vector2 screenPos,
                                                    ref float maxDist,
                                                    ref Vector3 closestHit,
                                                    ref Color colorClosestHit,
                                                float sqrMaxScreenDistance)
        {
            if (State == PCNodeState.INVISIBLE || currentMeshState != MeshState.LOADED)
            {
                return;
            }

            Mesh mesh = meshFilter.mesh;
            if (mesh == null)
            {
                return;
            }
            Bounds meshBounds = boundsInModelSpace;
            if (meshBounds.Contains(ray.origin) || meshBounds.IntersectRay(ray))
            {
                //print("Scanning Point Cloud with " + mesh.vertices.Length + " vertices.");
                int i = 0;
                foreach (Vector3 p in mesh.vertices)
                {
                    Vector3 pWorld = transform.TransformPoint(p);
                    Vector3 v = Camera.main.WorldToScreenPoint(pWorld);
                    float distancePointToCamera = Mathf.Abs(v.z);
                    if (distancePointToCamera < maxDist)
                    {
                        float sqrDistance = (new Vector2(v.x, v.y) - screenPos).sqrMagnitude;
                        if (sqrDistance < sqrMaxScreenDistance)
                        {
                            closestHit = pWorld;
                            colorClosestHit = mesh.colors[i];
                            maxDist = distancePointToCamera;
                        }
                    }
                    i++;
                }
            }
        }

    #endregion
}