extends SceneTree

func _initialize() -> void:
    call_deferred("check_plugin")

func check_plugin() -> void:
    for frame in range(30):
        await process_frame
    var editors := root.find_children("*", "Control", true, false)
    for control in editors:
        if control.get_script() != null and control.get_script().resource_path == "res://addons/dialogue_editor/DialogueEditor.cs":
            if control.find_child("GraphEdit", true, false) != null:
                print("PLUGIN CHECK PASSED: enabled editor plugin created its graph dock")
                EditorInterface.get_base_control().get_tree().quit(0)
                return
    push_error("Dialogue Editor dock was not loaded by the editor")
    EditorInterface.get_base_control().get_tree().quit(1)
