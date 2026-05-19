namespace TreeChoppingEditor;

public static class PlayShortcut
{
	[Shortcut( "tree_chopping.toggle-play", "Ctrl+Shift+P", ShortcutType.Window )]
	public static void TogglePlay()
	{
		EditorScene.TogglePlay();
	}
}
