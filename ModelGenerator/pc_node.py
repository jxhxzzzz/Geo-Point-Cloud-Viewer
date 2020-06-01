import pc_utils
import numpy as np


class PCNode:

    def __init__(self, points_by_class_dictionary):
        self.points_by_class = points_by_class_dictionary
        self.n_points_by_class = sum([n.shape[0] for n in self.points_by_class.values()])
        self.n_points = np.sum(self.n_points_by_class)

        counts = np.array([n.shape[0] for n in self.points_by_class.values()])
        classes = np.array(list(self.points_by_class.keys()))
        sorted_classes = classes[np.argsort(counts)]
        self.sorted_class_count = {c: self.points_by_class[c].shape[0] for c in sorted_classes}

    def get_all_xyz_points(self):
        t = tuple([self.points_by_class[c] for c in self.sorted_class_count.keys()])
        return np.vstack(t)

    def balanced_subsampling(self, n_selected_points):
        """Returns balanced subsample and remaining points by class"""

        if self.n_points <= n_selected_points:
            return self, None

        sampled = {}
        remaining = {}
        remaining_points = n_selected_points
        remaining_classes = len(self.sorted_class_count)
        for c, n in self.sorted_class_count.items():
            n_taken = int(min(remaining_points / remaining_classes, n)) if remaining_classes > 1 else remaining_points
            remaining_points -= n_taken
            remaining_classes -= 1
            sampled_points, not_sampled_points = pc_utils.random_subsampling(self.points_by_class[c], n_taken)
            sampled[c] = sampled_points
            if not_sampled_points is not None:
                remaining[c] = not_sampled_points

        sampled = PCNode(sampled)
        remaining = PCNode(remaining)
        assert sampled.n_points + remaining.n_points == self.n_points and sampled.n_points <= n_selected_points

        return sampled, remaining

    def split_octree(self, level):
        pass