extends Node

@export var destronoi_node : DestronoiNode

func _ready() -> void:
	# destronoi_node.destroy(1,1, 10.0)
	pass

func _unhandled_input(_event: InputEvent) -> void:
	if Input.is_action_just_pressed("debug_explode"):
		destronoi_node.destroy(2,2, 1)
