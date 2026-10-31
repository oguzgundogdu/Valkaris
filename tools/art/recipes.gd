# What each village sprite is made of. A building's stages should read as the same place getting bigger.
extends RefCounted

const ALL := [
	"main_1", "main_2", "main_3", "woodcutter_1", "woodcutter_2", "woodcutter_3", "quarry_1", "quarry_2", "quarry_3",
	"farm_1", "farm_2", "farm_3", "warehouse_1", "warehouse_2", "warehouse_3", "barracks_1", "barracks_2", "barracks_3",
	"wall_1", "wall_2", "wall_3", "marketplace_1", "marketplace_2", "marketplace_3", "watchtower_1", "watchtower_2", "watchtower_3",
	"plot", "scaffold",
]

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
			r.part("n:crops_dirtRow", spot, 0, 0.85)
			for offset in [-0.24, 0.0, 0.24]:
				r.part(crop, spot + Vector3(0, 0, offset), 0, 0.95)

func fence_line(from: Vector3, count: int, turn: float, piece := "fence", step := Vector3(1, 0, 0)) -> void:
	for i in count:
		r.part(piece, from + step * i, turn, 1.0)

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

# Quarry: a cut in the rock, then a working pit, then a full works with a shed and stone blocks.
func quarry_1() -> void:
	r.shadow(4.2, 3.6)
	r.part("rock-large", Vector3(-0.6, 0, -0.5), 20, 1.5)
	r.part("rock-small", Vector3(0.7, 0, 0.7), 0, 1.2)
	r.part("wall-block-half", Vector3(1.0, 0, -0.5), 0, 0.5)
func quarry_2() -> void:
	r.shadow(5, 4.4)
	r.part("rock-large", Vector3(-0.9, 0, -0.8), 20, 1.8)
	r.part("rock-wide", Vector3(1.0, 0, -1.0), 80, 1.3)
	r.part("rock-small", Vector3(-1.0, 0, 1.1), 0, 1.2)
	r.part("cart", Vector3(1.0, 0, 0.9), 30, 1.0)
	r.part("wall-block-half", Vector3(0.0, 0, 0.9), 0, 0.55)
	r.part("wall-block-half", Vector3(0.0, 0.27, 0.9), 20, 0.4)
func quarry_3() -> void:
	r.shadow(5.8, 5)
	r.part("rock-large", Vector3(-1.3, 0, -1.2), 20, 2.1)
	r.part("rock-wide", Vector3(0.9, 0, -1.5), 80, 1.5)
	r.house(Vector3(1.5, 0, 0.5), 1, 1, 1, true, "roof-point", 1.0, "window-small")
	r.part("cart-high", Vector3(-0.2, 0, 1.3), 30, 1.0)
	for spot in [Vector3(-1.4, 0, 1.0), Vector3(-1.4, 0, 1.6), Vector3(-1.9, 0, 1.3)]:
		r.part("wall-block-half", spot, 0, 0.55)
	r.part("wall-block-half", Vector3(-1.4, 0.27, 1.3), 0, 0.55)
	r.part("poles", Vector3(0.2, 0, -0.2), -90, 1.4)

# Farm: a hut and a field, then a barn with fences, then fields around a windmill.
func farm_1() -> void:
	r.shadow(3.6, 3.6)
	r.house(Vector3(-0.7, 0, -0.7), 1, 1, 1, false, "roof-gable", 1.0, "window-small")
	field(Vector3(0.4, 0, -0.6), 2)
func farm_2() -> void:
	r.shadow(4.8, 4.6)
	r.house(Vector3(-0.9, 0, -0.8), 2, 1, 1, false, "roof-high-gable", 1.1, "window-small")
	field(Vector3(-1.2, 0, 0.5), 3)
	fence_line(Vector3(-2.0, 0, 2.3), 3, -90)
func farm_3() -> void:
	r.shadow(5.8, 5.4)
	r.house(Vector3(-1.3, 0, -1.2), 2, 1, 1, false, "roof-high-gable", 1.1, "window-small")
	r.house(Vector3(1.2, 0, -1.4), 1, 1, 2, true, "roof-point", 0.9, "window-small", false)
	r.part("windmill", Vector3(1.75, 1.5, -0.9), 0, 0.5)
	field(Vector3(-1.9, 0, 0.3), 5, "n:crops_cornStageD")
	fence_line(Vector3(-2.5, 0, 2.4), 5, -90)

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

# Wall: a low stone stretch, a wall with a pillar, then a gatehouse. The ring around the village comes later.
func wall_1() -> void:
	r.shadow(4, 2.6)
	for i in 3:
		r.part("wall-half", Vector3(i - 1, 0, 0), -90, 1.0)
func wall_2() -> void:
	r.shadow(4.4, 2.8)
	for i in 3:
		r.part("wall", Vector3(i - 1, 0, 0), -90, 1.1)
	r.part("pillar-stone", Vector3(-1.65, 0, 0.55), 0, 1.8)
	r.part("pillar-stone", Vector3(1.65, 0, 0.55), 0, 1.8)
func wall_3() -> void:
	r.shadow(5, 3)
	r.house(Vector3(-1.6, 0, 0), 1, 1, 2, true, "roof-point", 1.0, "window-stone", false)
	r.house(Vector3(1.6, 0, 0), 1, 1, 2, true, "roof-point", 1.0, "window-stone", false)
	r.part("wall-doorway-round", Vector3(0, 0, 0), -90, 2.2)
	r.part("banner-red", Vector3(-1.0, 0.8, 1.05), -90, 0.9)

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
	r.shadow(3.2, 3.2)
	r.house(Vector3.ZERO, 1, 1, 4, true, "roof-high-point", 1.2, "window-stone")
	r.part("banner-red", Vector3(0.6, 2.5, 0), 0, 1.0)

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
