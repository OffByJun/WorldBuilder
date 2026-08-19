using UnityEngine;

namespace WorldBuilder.Editor
{
    public static class WorldBuilderSceneGUI
    {
        public static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;
    }
}
