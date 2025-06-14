extends Node

@export var destronoi_node : DestronoiNode

func _ready() -> void:
	destronoi_node.destroy(5,5, 10.0)
