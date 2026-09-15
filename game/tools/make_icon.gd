extends SceneTree

# Rasterizes an SVG to one PNG per icon size, with Godot's own SVG renderer so no image tools are
# needed. Run by tools/make-icon.ps1, which packs the PNGs into icon.ico; not part of the game, and
# left out of the export.
#   godot --headless --path game --script res://tools/make_icon.gd -- <svg> <out dir>

const SIZES := [16, 24, 32, 48, 64, 128, 256]

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		push_error("usage: -- <svg path> <output directory>")
		quit(1)
		return

	var svg := FileAccess.get_file_as_bytes(args[0])
	if svg.is_empty():
		push_error("could not read %s" % args[0])
		quit(1)
		return

	for size in SIZES:
		var image := Image.new()
		# The SVG is drawn at 256, so each size is a scale of that.
		if image.load_svg_from_buffer(svg, size / 256.0) != OK:
			push_error("could not rasterize the icon at %d px" % size)
			quit(1)
			return
		image.save_png(args[1].path_join("icon_%d.png" % size))

	quit(0)
