# What each village sprite is made of. A building's stages should read as the same place getting bigger.
extends RefCounted

const ALL := [
	"main_1", "main_2", "main_3", "woodcutter_1", "woodcutter_2", "woodcutter_3", "quarry_1", "quarry_2", "quarry_3",
	"farm_1", "farm_2", "farm_3", "warehouse_1", "warehouse_2", "warehouse_3", "barracks_1", "barracks_2", "barracks_3",
	"wall_1", "wall_2", "wall_3", "marketplace_1", "marketplace_2", "marketplace_3", "watchtower_1", "watchtower_2", "watchtower_3",
	"plot", "scaffold",
	"house_1", "house_2", "house_3", "house_4", "tree_1", "tree_2", "tree_3", "tree_4", "bush_1", "bush_2", "rock_1", "rock_2",
	"flowers_1", "flowers_2", "fence_1", "crops_1", "crops_2",
	"ring_across_1", "ring_across_2", "ring_across_3", "ring_along_1", "ring_along_2", "ring_along_3", "ring_post_1", "ring_post_2", "ring_post_3",
]

# One roof colour per building, so it stays recognisable at every stage. Sprites without a name here keep the kit's colours.
const ROOFS := {
	"main": Color("c8503f"), "woodcutter": Color("5f8f4e"), "quarry": Color("6f7a8c"), "farm": Color("d0a040"),
	"warehouse": Color("b5683c"), "barracks": Color("3f5f96"), "wall": Color("565d6b"), "watchtower": Color("2f9a8c"),
	"house": Color("9c7050"), "ring": Color("565d6b"),
}

const STONE := Color(0.62, 0.68, 0.76)
const LEAF := Color(0.66, 0.72, 0.46)

var r

func _init(renderer) -> void:
	r = renderer

func logs(at: Vector3, count := 3) -> void:
	for i in count:
		r.part("n:log", at + Vector3(0, 0, i * 0.28 - 0.28), 0, 0.9)
	if count >= 3:
		r.part("n:log", at + Vector3(0, 0.22, 0), 0, 0.9)

func field(at: Vector3, rows: int, crop := "n:crops_wheatStageB", length := 3) -> void:
	for row in rows:
		for i in length:
			var spot := at + Vector3(row * 0.85, 0, i * 0.85)
			r.tinted("n:crops_dirtRow", spot, 0, 0.85, Color(0.5, 0.38, 0.3))
			for offset in [-0.24, 0.0, 0.24]:
				r.tinted(crop, spot + Vector3(0, 0, offset), 0, 0.95, LEAF if crop == "n:crops_cornStageD" else Color.WHITE)

func fence_line(from: Vector3, count: int, turn: float, piece := "fence", step := Vector3(1, 0, 0)) -> void:
	for i in count:
		r.part(piece, from + step * i, turn, 1.0)

func stones(at: Vector3, layers := 1) -> void:
	for spot in [Vector3(0, 0, 0), Vector3(0.55, 0, 0), Vector3(0.28, 0, 0.5)]:
		r.part("wall-block-half", at + spot, 0, 0.5)
	if layers > 1:
		r.part("wall-block-half", at + Vector3(0.28, 0.25, 0.15), 15, 0.5)

func stakes(at: Vector3, size: float) -> void:
	for corner in [Vector3(1, 0, 1), Vector3(1, 0, -1), Vector3(-1, 0, 1), Vector3(-1, 0, -1)]:
		r.part("pillar-wood", at + corner * size, 0, 0.8)

# Main Building: a timber hall, then a stone hall, then a keep with a tower.
func main_1() -> void:
	r.shadow(4, 3.5)
	r.house(Vector3.ZERO, 2, 1, 1, false, "roof-gable", 1.2)
func main_2() -> void:
	r.shadow(4.6, 4.6)
	r.house(Vector3.ZERO, 2, 2, 2, false, "roof-high-point", 1.1)
	r.part("banner-red", Vector3(1.1, 1.0, 0.5), 0, 1.0)
func main_3() -> void:
	r.shadow(5.4, 5.4)
	r.house(Vector3(0, 0, 0), 2, 2, 2, true, "roof-high-point", 1.25, "window-stone")
	r.house(Vector3(-0.65, 0, -1.9), 1, 1, 3, true, "roof-high-point", 1.25, "window-stone", false)
	r.part("banner-red", Vector3(1.25, 1.3, 0.6), 0, 1.15)
	r.part("banner-red", Vector3(1.25, 1.3, -0.6), 0, 1.15)

# Woodcutter: a shed with logs, a bigger shed, then a sawmill with a blade and a cart.
func woodcutter_1() -> void:
	r.shadow(3.2, 3)
	r.house(Vector3(-0.3, 0, -0.3), 1, 1, 1, false, "roof-gable", 1.1, "window-small")
	logs(Vector3(0.9, 0, 0.5), 2)
	r.part("n:stump_round", Vector3(-0.8, 0, 1.0), 0, 1.1)
func woodcutter_2() -> void:
	r.shadow(4, 3.8)
	r.house(Vector3(-0.4, 0, -0.3), 1, 2, 1, false, "roof-gable", 1.15, "window-small")
	logs(Vector3(1.1, 0, 0.3))
	logs(Vector3(1.1, 0, -0.9))
	r.part("n:stump_round", Vector3(-0.8, 0, 1.5), 0, 1.1)
func woodcutter_3() -> void:
	r.shadow(5, 4.6)
	r.house(Vector3(-0.6, 0, -0.3), 2, 2, 1, false, "roof-gable", 1.15, "window-small")
	r.part("blade", Vector3(1.35, 0.7, 0.6), 90, 0.75)
	r.part("cart", Vector3(1.6, 0, -0.8), 20, 1.0)
	logs(Vector3(0.3, 0, 1.9))
	logs(Vector3(-0.9, 0, 1.9))

# Quarry: always a working yard. A shed, stones and a cart by the rock, then more of each, then a stone shed and a hoist.
func quarry_1() -> void:
	r.shadow(4.8, 4.4)
	r.part("rock-large", Vector3(-1.1, 0, -1.0), 20, 1.6)
	r.house(Vector3(0.9, 0, -0.8), 1, 1, 1, false, "roof-point", 1.0, "window-small")
	r.part("cart", Vector3(0.9, 0, 0.8), 30, 0.95)
	stones(Vector3(-0.9, 0, 0.5))
func quarry_2() -> void:
	r.shadow(5.4, 5)
	r.part("rock-large", Vector3(-1.2, 0, -1.2), 20, 1.9)
	r.part("rock-wide", Vector3(-1.9, 0, 0.1), 80, 1.2)
	r.house(Vector3(1.0, 0, -1.0), 2, 1, 1, false, "roof-gable", 0.95, "window-small")
	r.part("cart", Vector3(1.2, 0, 0.8), 30, 1.0)
	stones(Vector3(-0.7, 0, 0.9), 2)
	stones(Vector3(0.0, 0, 0.1))
func quarry_3() -> void:
	r.shadow(6, 5.4)
	r.part("rock-large", Vector3(-1.4, 0, -1.4), 20, 2.2)
	r.part("rock-wide", Vector3(-2.1, 0, 0.1), 80, 1.5)
	r.house(Vector3(1.2, 0, -1.0), 2, 1, 2, true, "roof-gable", 1.0, "window-small")
	r.part("cart-high", Vector3(1.3, 0, 1.0), 30, 1.0)
	r.part("poles", Vector3(-0.2, 0, -0.5), -90, 1.6)
	stones(Vector3(-1.1, 0, 1.0), 2)
	stones(Vector3(-0.3, 0, 1.4), 2)
	stones(Vector3(-0.1, 0, 0.3), 2)

# Farm: fenced fields, then a barn beside them, then a large windmill too.
func farm_1() -> void:
	r.shadow(4.2, 4.2)
	field(Vector3(-1.2, 0, -0.9), 3)
	fence_line(Vector3(-1.6, 0, 1.6), 3, -90)
	r.part("cart", Vector3(1.5, 0, 0.1), 80, 0.85)
func farm_2() -> void:
	r.shadow(5, 4.8)
	r.house(Vector3(-0.9, 0, -1.3), 2, 1, 1, false, "roof-high-gable", 1.15, "window-small")
	field(Vector3(-1.5, 0, 0.0), 4)
	fence_line(Vector3(-2.0, 0, 2.3), 4, -90)
func farm_3() -> void:
	r.shadow(6, 5.4)
	r.house(Vector3(-1.5, 0, -1.5), 2, 1, 1, false, "roof-high-gable", 1.15, "window-small")
	r.house(Vector3(1.3, 0, -1.3), 1, 1, 2, true, "roof-point", 1.15, "window-small", false)
	r.part("windmill", Vector3(1.95, 1.75, -1.0), 0, 1.0)
	field(Vector3(-1.7, 0, 0.1), 4, "n:crops_cornStageD")
	fence_line(Vector3(-2.2, 0, 2.5), 4, -90)

# Warehouse: a store, a long store, then a stone-based depot with goods outside.
func warehouse_1() -> void:
	r.shadow(3.2, 3)
	r.house(Vector3.ZERO, 1, 1, 1, false, "roof-gable", 1.25, "window-small")
func warehouse_2() -> void:
	r.shadow(4, 4)
	r.house(Vector3.ZERO, 2, 2, 1, false, "roof-gable", 1.15, "window-small")
	r.part("stall-bench", Vector3(1.6, 0, 0.4), 0, 1.0)
func warehouse_3() -> void:
	r.shadow(5, 5)
	r.house(Vector3.ZERO, 3, 2, 1, true, "roof-gable", 1.15, "window-small")
	r.house(Vector3(-0.6, 1.15, 0), 2, 2, 1, false, "roof-high-gable", 1.15, "window-small", false)
	r.part("cart", Vector3(2.1, 0, 0.6), 10, 1.0)

# Barracks: a fenced yard with a hut, then a stone hall, then a hall with a yard, banners and a tower.
func barracks_1() -> void:
	r.shadow(3.8, 3.6)
	r.house(Vector3(-0.5, 0, -0.5), 1, 1, 1, false, "roof-point", 1.1)
	fence_line(Vector3(-1.2, 0, 1.0), 3, -90)
	r.part("banner-red", Vector3(0.05, 0.1, -0.5), 0, 0.9)
func barracks_2() -> void:
	r.shadow(4.6, 4)
	r.house(Vector3(0, 0, -0.3), 2, 1, 1, true, "roof-gable", 1.3, "window-stone")
	r.part("banner-red", Vector3(1.3, 0.2, -0.3), 0, 1.1)
	fence_line(Vector3(-1.2, 0, 1.3), 3, -90)
func barracks_3() -> void:
	r.shadow(5.4, 5)
	r.house(Vector3(0, 0, -0.5), 3, 1, 1, true, "roof-gable", 1.25, "window-stone")
	r.house(Vector3(-1.85, 0, -0.5), 1, 1, 2, true, "roof-high-point", 1.05, "window-stone", false)
	r.part("banner-red", Vector3(1.85, 0.2, -0.5), 0, 1.1)
	r.part("banner-red", Vector3(0.6, 0.15, 0.1), -90, 1.1)
	fence_line(Vector3(-1.2, 0, 1.6), 3, -90)
	for i in 3:
		r.part("pillar-wood", Vector3(-0.8 + i * 0.8, 0, 0.8), 0, 0.5)

# Wall: the gatehouse in the ring. Timber gate, stone gate with pillars, then a gate between two towers. Faces the camera.
func wall_1() -> void:
	r.face_camera()
	r.shadow(3.6, 2.2)
	r.part("fence-gate", Vector3.ZERO, 0, 1.6)
	r.part("pillar-wood", Vector3(0.45, 0, -0.95), 0, 1.5)
	r.part("pillar-wood", Vector3(0.45, 0, 0.95), 0, 1.5)
func wall_2() -> void:
	r.face_camera()
	r.shadow(4, 2.6)
	r.part("wall-doorway-round", Vector3.ZERO, 0, 1.8)
	r.part("pillar-stone", Vector3(0.1, 0, -1.15), 0, 2.2)
	r.part("pillar-stone", Vector3(0.1, 0, 1.15), 0, 2.2)
func wall_3() -> void:
	r.face_camera()
	r.shadow(5, 3)
	r.house(Vector3(0, 0, -1.55), 1, 1, 2, true, "roof-point", 1.05, "window-stone", false)
	r.house(Vector3(0, 0, 1.55), 1, 1, 2, true, "roof-point", 1.05, "window-stone", false)
	r.part("wall-doorway-round", Vector3.ZERO, 0, 2.1)
	r.part("banner-red", Vector3(0.6, 0.9, -1.05), 0, 0.9)
	r.part("banner-red", Vector3(0.6, 0.9, 1.05), 0, 0.9)

# Marketplace: one stall, a row of stalls, then a square with a fountain.
func marketplace_1() -> void:
	r.shadow(2.8, 2.6)
	r.part("stall-red", Vector3.ZERO, 0, 1.5)
func marketplace_2() -> void:
	r.shadow(4.2, 4)
	r.part("stall-red", Vector3(-0.6, 0, -0.8), 0, 1.4)
	r.part("stall-green", Vector3(0.9, 0, -0.8), 0, 1.4)
	r.part("stall-bench", Vector3(0.1, 0, 0.7), 0, 1.2)
func marketplace_3() -> void:
	r.shadow(5.4, 5)
	r.part("stall-red", Vector3(-1.4, 0, -1.2), 0, 1.4)
	r.part("stall-green", Vector3(0.2, 0, -1.4), 0, 1.4)
	r.part("stall-red", Vector3(1.7, 0, -0.6), -90, 1.4)
	r.part("fountain-round", Vector3(-0.2, 0, 0.8), 0, 1.3)
	r.part("cart", Vector3(1.7, 0, 1.2), 0, 1.0)

# Watchtower: a lookout on stilts, a tall timber tower, then a stone tower.
func watchtower_1() -> void:
	r.shadow(2.6, 2.6)
	for corner in [Vector3(0.6, 0, 0.6), Vector3(0.6, 0, -0.6), Vector3(-0.6, 0, 0.6), Vector3(-0.6, 0, -0.6)]:
		r.part("pillar-wood", corner, 0, 2.2)
	r.part("planks", Vector3(0, 2.2, 0), 0, 1.5)
	r.part("fence", Vector3(0, 2.2, 0.2), 0, 1.2)
	r.part("fence", Vector3(0, 2.2, 0), -90, 1.2)
	r.part("roof-point", Vector3(0, 2.9, 0), 0, 1.3)
func watchtower_2() -> void:
	r.shadow(3, 3)
	r.house(Vector3.ZERO, 1, 1, 3, false, "roof-high-point", 1.1, "window-small")
func watchtower_3() -> void:
	r.shadow(3.6, 3.6)
	r.house(Vector3.ZERO, 1, 1, 3, true, "roof-flat", 1.25, "window-stone")
	var top := 3.75
	for i in 5:
		var along := -0.625 + i * 0.3125
		for spot in [Vector3(0.6, 0, along), Vector3(-0.6, 0, along), Vector3(along, 0, 0.6), Vector3(along, 0, -0.6)]:
			if i % 2 == 0:
				r.part("wall-block-half", spot + Vector3(0, top, 0), 0, 0.4)
	r.part("roof-point", Vector3(0, top, 0), 0, 0.9)
	r.part("pillar-wood", Vector3(0, top + 0.7, 0), 0, 1.4)
	r.part("banner-green", Vector3(0.05, top + 0.8, 0), 0, 1.3)
	r.part("lantern", Vector3(0.55, top + 0.15, 0.55), 0, 1.0)

# An empty plot: bare ground with four stakes.
func plot() -> void:
	r.disc(Vector3(0, 0.005, 0), 4, 4, Color(0.43, 0.33, 0.2, 0.9))
	stakes(Vector3.ZERO, 1.1)

# Drawn over a building being worked on.
func scaffold() -> void:
	for at in [Vector3(1.3, 0, 0.7), Vector3(1.3, 0, -0.7), Vector3(0.1, 0, 1.3), Vector3(-1.1, 0, 1.3)]:
		r.part("pillar-wood", at, 0, 2.2)
	r.part("planks-half", Vector3(1.0, 1.1, 0), 0, 1.5)
	r.part("planks-half", Vector3(-0.4, 1.1, 1.0), 90, 1.5)
	r.part("poles-horizontal", Vector3(1.0, 1.7, 0), 0, 1.4)
	r.part("poles-horizontal", Vector3(-0.4, 1.7, 0.8), -90, 1.4)

# Village houses: smaller than any building, so they read as homes around it.
func house_1() -> void:
	r.shadow(2, 2)
	r.house(Vector3.ZERO, 1, 1, 1, false, "roof-gable", 0.75, "window-small")
func house_2() -> void:
	r.shadow(2.4, 2)
	r.house(Vector3.ZERO, 2, 1, 1, false, "roof-gable", 0.7)
func house_3() -> void:
	r.shadow(2, 2)
	r.house(Vector3.ZERO, 1, 1, 2, false, "roof-point", 0.72, "window-small")
func house_4() -> void:
	r.shadow(2.4, 2)
	r.house(Vector3.ZERO, 2, 1, 1, true, "roof-gable", 0.7, "window-small")

func tree_1() -> void:
	r.shadow(1.9, 1.9)
	r.tinted("n:tree_oak", Vector3.ZERO, 0, 1.55, LEAF)
func tree_2() -> void:
	r.shadow(1.7, 1.7)
	r.tinted("n:tree_default", Vector3.ZERO, 40, 1.5, LEAF)
func tree_3() -> void:
	r.shadow(1.7, 1.7)
	r.tinted("n:tree_detailed", Vector3.ZERO, 0, 1.5, LEAF)
func tree_4() -> void:
	r.shadow(1.9, 1.9)
	r.tinted("n:tree_fat", Vector3.ZERO, 0, 1.55, LEAF)
func bush_1() -> void:
	r.shadow(1.5, 1.5)
	r.tinted("n:plant_bushLarge", Vector3.ZERO, 0, 2.6, LEAF)
func bush_2() -> void:
	r.shadow(1.2, 1.2)
	r.tinted("n:plant_bush", Vector3.ZERO, 0, 2.8, LEAF)
func rock_1() -> void:
	r.shadow(1.5, 1.5)
	r.tinted("n:rock_smallA", Vector3.ZERO, 0, 2.6, STONE)
func rock_2() -> void:
	r.shadow(1.5, 1.5)
	r.tinted("n:rock_smallF", Vector3.ZERO, 50, 2.4, STONE)
func flowers_1() -> void:
	r.part("n:flower_yellowA", Vector3(0.3, 0, 0), 0, 2.2)
	r.part("n:flower_redA", Vector3(-0.3, 0, 0.3), 0, 2.2)
	r.tinted("n:grass_large", Vector3(0, 0, -0.3), 0, 1.6, LEAF)
func flowers_2() -> void:
	r.part("n:flower_purpleA", Vector3(-0.3, 0, -0.1), 0, 2.2)
	r.part("n:flower_yellowA", Vector3(0.3, 0, 0.2), 0, 2.2)
	r.tinted("n:grass_large", Vector3(0.1, 0, 0.4), 0, 1.6, LEAF)
func fence_1() -> void:
	fence_line(Vector3(-0.5, 0, 0), 2, -90)
func crops_1() -> void:
	field(Vector3(-0.42, 0, -0.42), 2, "n:crops_wheatStageB", 2)
func crops_2() -> void:
	field(Vector3(-0.42, 0, -0.42), 2, "n:crops_cornStageD", 2)

# The Wall's ring, drawn piece by piece around the village: "across" runs along the screen, "along" runs away from the viewer.
# Timber fence, then a low stone wall, then a full wall. No shadows, since the pieces overlap each other.
func ring_piece(turn: float, offset: Vector3, piece: String) -> void:
	r.face_camera()
	r.part(piece, offset, turn, 1.02)
func ring_across_1() -> void:
	ring_piece(0, Vector3.ZERO, "fence")
func ring_across_2() -> void:
	ring_piece(0, Vector3(-0.5, 0, 0), "wall-half")
func ring_across_3() -> void:
	ring_piece(0, Vector3(-0.5, 0, 0), "wall")
func ring_along_1() -> void:
	ring_piece(90, Vector3.ZERO, "fence")
func ring_along_2() -> void:
	r.part("wall-block-half", Vector3.ZERO, 0, 0.72)
func ring_along_3() -> void:
	r.part("wall-block", Vector3.ZERO, 0, 0.72)
func ring_post_1() -> void:
	r.part("pillar-wood", Vector3.ZERO, 0, 1.4)
func ring_post_2() -> void:
	r.part("pillar-stone", Vector3.ZERO, 0, 1.5)
func ring_post_3() -> void:
	r.house(Vector3.ZERO, 1, 1, 2, true, "roof-point", 0.78, "window-stone", false)
