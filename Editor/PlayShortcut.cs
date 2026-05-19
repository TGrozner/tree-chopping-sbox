namespace TreeChoppingEditor;

public static class PlayShortcut
{
	[Shortcut( "tree_chopping.toggle-play", "F5", ShortcutType.Window )]
	public static void TogglePlay()
	{
		EditorScene.TogglePlay();
	}
}
