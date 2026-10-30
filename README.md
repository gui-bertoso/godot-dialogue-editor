# Godot Dialogue Editor

A visual dialogue editor for Godot .NET, written in C#. Created by [Guilherme Bertoso](https://github.com/gui-bertoso), extracted from a private version of Project Horizons and made available as a standalone addon.

Build branching conversations with Start, Dialogue, Choices, Random and End nodes. Connect ports, edit responses, validate the graph and save it as a readable `.dalge` file. The editor lives in Godot's bottom panel and follows the editor theme.

## Requirements

Godot **.NET** is required; the standard Godot download cannot run C# plugins. This extraction was built and checked with **Godot 4.8 beta 1 .NET** and **.NET 10**, matching the source project. Other Godot versions have not been verified. The included example project pins that SDK version.

## Install in your project

1. Copy `addons/dialogue_editor` into your project's `addons` directory.
2. Build your C# project in Godot or with `dotnet build`.
3. Enable **Dialogue Editor** under **Project → Project Settings → Plugins**.
4. Open **Dialogue Editor** in the bottom panel.

The addon includes its `.dalge` parser. No game scripts, fonts or external packages are needed beyond Godot's C# SDK. If you already have a `DalgeParser` or `DalgeData` class in your project, remove the duplicate definitions before installing this copy.

## Try the example

Open the repository's `project.godot` in the matching Godot .NET editor, build it, and enable the plugin if it is not already enabled. Open `addons/dialogue_editor/output/dialogue_preview.dalge` from the panel's File menu for a small branching example.

Right-click the graph to add nodes. Drag between ports to connect them. A Dialogue node has two responses; Choices and Random nodes have three outputs. Use Validate to check the graph, then Save As to store it under a folder such as `res://dialogues/`.

The repository is a small editor host; it has no playable main scene. Copy only the addon folder into another project.

## File format and runtime integration

`.dalge` files store named node sections with `type`, `position`, `text` and indexed connections. For example:

```ini
[start]
type=start
position=40,80
next=hello

[hello]
type=dialogue
position=320,80
text=Hello, traveller.
option_0_text=Goodbye
option_0_next=end

[end]
type=end
position=680,80
```

`DalgeParser.Load("res://dialogues/example.dalge")` returns a dictionary of `DalgeData` nodes. Follow `NextByPort` in your own dialogue runner; response labels are in `OptionTextByIndex`. Text supports escaped newlines (`\n`) and backslashes (`\\`).

This addon edits dialogue data. Player controllers, NPC interaction and dialogue playback UI belong to the host game and are not included. For exported games, include `*.dalge` in Godot's non-resource export filter.

## Development

```sh
dotnet build
godot --headless --editor --path . --quit-after 60
godot --headless --path . res://tests/check_dialogues.tscn
```

Replace `godot` with your Godot .NET executable. The first Godot command opens and closes the editor with the plugin enabled. The scene check covers graph round trips, choice/random port indices, Unicode escaping, malformed input and failed-save protection. They write temporary files under `/tmp` and do not alter the example dialogue.

## License

[MIT](LICENSE). Extracted from a private version of Project Horizons and released separately as a reusable addon.
