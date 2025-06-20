extends Node
class_name fragmentation_component

@export var destronoi_node : DestronoiNode

func fragment() -> void:
	destronoi_node.destroy(2,2, 1)