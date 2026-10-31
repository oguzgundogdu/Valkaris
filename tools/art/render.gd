# Renders the village sprites from the CC0 kits: godot-mono --path tools/art --script render.gd -- [out_dir]
# Sprites share one camera, scale and light, so they fit together. The game only ever loads the PNGs.
extends SceneTree

const Recipes := preload("res://recipes.gd")
const SIZE := 512
const UNITS_TALL := 9.0
const LOOK_AT := Vector3(0, 2.4, 0)
const DIRS := {"": "models/town/", "n:": "models/nature/", "c:": "models/characters/"}

var world: Node3D
var viewport: SubViewport
var parts: Node3D
var camera: Camera3D
var used := {}
var roof_colour := Color(0, 0, 0, 0)
var roof_textures := {}
var out_dir := ProjectSettings.globalize_path("res://../../assets/village")

func _init() -> void:
	call_deferred("run")

func run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() > 0:
		out_dir = args[0]
	DirAccess.make_dir_recursive_absolute(out_dir)
	build_stage()
	for name in Recipes.ALL:
		clear()
		roof_colour = Recipes.ROOFS.get(name.split("_")[0], Color(0, 0, 0, 0))
		Recipes.new(self).call(name)
		await save(name)
	print("Ground centre on the canvas: ", camera.unproject_position(Vector3.ZERO), ", pixels per unit: ", SIZE / UNITS_TALL)
	print("Models used: ", " ".join(used.keys()))
	quit()

func build_stage() -> void:
	viewport = SubViewport.new()
	viewport.size = Vector2i(SIZE, SIZE)
	viewport.transparent_bg = true
	viewport.own_world_3d = true
	viewport.msaa_3d = Viewport.MSAA_8X
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	world = Node3D.new()
	viewport.add_child(world)
	parts = Node3D.new()
	world.add_child(parts)

	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = UNITS_TALL
	world.add_child(camera)
	camera.look_at_from_position(LOOK_AT + Vector3(20, 17, 20), LOOK_AT)

	# Low energies on purpose: higher ones wash the kit's bright colours out in this renderer.
	var environment := Environment.new()
	environment.background_mode = Environment.BG_CLEAR_COLOR
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(1.0, 0.93, 0.84)
	environment.ambient_light_energy = 0.17
	var holder := WorldEnvironment.new()
	holder.environment = environment
	world.add_child(holder)

	var sun := DirectionalLight3D.new()
	sun.light_energy = 0.36
	sun.light_color = Color(1.0, 0.9, 0.74)
	sun.shadow_enabled = true
	sun.rotation_degrees = Vector3(-52, 15, 0)
	world.add_child(sun)

func clear() -> void:
	for child in parts.get_children():
		parts.remove_child(child)
		child.free()
	parts.rotation_degrees.y = 0

## Turns the sprite so its +x face looks straight at the camera, for pieces that line up along the screen.
func face_camera() -> void:
	parts.rotation_degrees.y = -45

## Adds one model: "wall", or "n:crop_carrot" and "c:character-male-a" from the other kits.
func part(id: String, position := Vector3.ZERO, turn := 0.0, scale := 1.0) -> Node3D:
	used[id] = true
	var prefix := "n:" if id.begins_with("n:") else ("c:" if id.begins_with("c:") else "")
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	document.append_from_file(DIRS[prefix] + id.trim_prefix(prefix) + ".glb", state)
	var node: Node3D = document.generate_scene(state)
	node.position = position
	node.rotation_degrees.y = turn
	node.scale = Vector3.ONE * scale
	parts.add_child(node)
	for mesh in node.find_children("*", "MeshInstance3D", true, false):
		for i in mesh.mesh.get_surface_count():
			var material: BaseMaterial3D = mesh.mesh.surface_get_material(i).duplicate()
			material.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
			material.emission_enabled = false
			material.metallic = 0
			material.roughness = 1
			material.metallic_specular = 0.1
			if id.begins_with("roof") and roof_colour.a > 0:
				material.albedo_texture = roof_texture(material.albedo_texture, roof_colour)
			mesh.set_surface_override_material(i, material)
	return node

## Adds a model with its colours multiplied, for pieces the kit makes too pale.
func tinted(id: String, position: Vector3, turn: float, scale: float, tint: Color) -> void:
	var node := part(id, position, turn, scale)
	for mesh in node.find_children("*", "MeshInstance3D", true, false):
		for i in mesh.mesh.get_surface_count():
			var material: BaseMaterial3D = mesh.get_surface_override_material(i)
			material.albedo_color *= tint

## The kit's palette with its roof columns repainted in one colour, keeping their light and dark.
func roof_texture(texture: Texture2D, colour: Color) -> Texture2D:
	if roof_textures.has(colour):
		return roof_textures[colour]
	var image := texture.get_image()
	image.decompress()
	image.convert(Image.FORMAT_RGBA8)
	for column in [0, 64, 192]:
		var light := image.get_pixel(column + 47, 396).get_luminance()
		for y in range(384, image.get_height()):
			for x in range(column, column + 64):
				var shade := image.get_pixel(x, y).get_luminance() / light
				image.set_pixel(x, y, Color(colour.r * shade, colour.g * shade, colour.b * shade))
	roof_textures[colour] = ImageTexture.create_from_image(image)
	return roof_textures[colour]

## A soft dark patch on the ground, since a transparent sprite has nothing to catch a real shadow.
func shadow(size_x: float, size_z: float, strength := 0.34) -> void:
	disc(Vector3(0.12, 0.005, 0.12), size_x, size_z, Color(0.05, 0.04, 0.02, strength))

func disc(position: Vector3, size_x: float, size_z: float, colour: Color) -> void:
	var gradient := Gradient.new()
	gradient.set_color(0, colour)
	gradient.set_offset(0, 0.45)
	gradient.set_color(1, Color(colour, 0))
	var texture := GradientTexture2D.new()
	texture.gradient = gradient
	texture.fill = GradientTexture2D.FILL_RADIAL
	texture.fill_from = Vector2(0.5, 0.5)
	texture.fill_to = Vector2(0.5, 0)
	var material := StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.albedo_texture = texture
	var mesh := PlaneMesh.new()
	mesh.size = Vector2(size_x, size_z)
	mesh.material = material
	var node := MeshInstance3D.new()
	node.mesh = mesh
	node.position = position
	parts.add_child(node)

## Walls on the outside of a block of cells, with a roof on each cell. Walls face +x and +z towards the camera.
func house(centre: Vector3, wide: int, deep: int, floors: int, stone: bool, roof: String, cell := 1.0, windows := "window-shutters", door := true) -> void:
	var origin := centre - Vector3((wide - 1) / 2.0, 0, (deep - 1) / 2.0) * cell
	var kind := "wall-" if stone else "wall-wood-"
	var plain := "wall" if stone else "wall-wood"
	for floor_index in floors:
		for x in wide:
			for z in deep:
				var at := origin + Vector3(x, floor_index, z) * cell
				var front := func(i: int) -> String:
					if door and floor_index == 0 and i == 0:
						return kind + "door"
					return kind + windows if i % 2 == 1 or floor_index > 0 else plain
				if x == wide - 1:
					part(front.call(z), at, 0, cell)
				if z == deep - 1:
					part(front.call(x + 1), at, -90, cell)
				if x == 0:
					part(plain, at, 180, cell)
				if z == 0:
					part(plain, at, 90, cell)
	for x in wide:
		for z in deep:
			part(roof, origin + Vector3(x, floors, z) * cell, 0 if deep >= wide else 90, cell)

## Warmer and a little less saturated than the kit, to sit with the painted map.
func save(name: String) -> void:
	for i in 3:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := viewport.get_texture().get_image()
	image.convert(Image.FORMAT_RGBA8)
	for y in image.get_height():
		for x in image.get_width():
			var c := image.get_pixel(x, y)
			if c.a <= 0.0:
				continue
			var grey := c.get_luminance()
			image.set_pixel(x, y, Color(lerpf(grey, c.r, 0.82) * 1.03, lerpf(grey, c.g, 0.82) * 1.0, lerpf(grey, c.b, 0.82) * 0.93, c.a))
	image.save_png(out_dir.path_join(name + ".png"))
