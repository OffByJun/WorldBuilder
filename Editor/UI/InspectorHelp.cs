using UnityEngine.UIElements;

namespace WorldBuilder.Editor
{
    public static class InspectorHelp
    {
        public static VisualElement Build(string toolName, string helpKey)
        {
            VisualElement container = new VisualElement();

            Label header = new Label(toolName);
            header.AddToClassList("tool-inspector-header");
            container.Add(header);

            Foldout foldout = new Foldout
            {
                text = WorldBuilderLocalization.Get("help.title"),
                value = false
            };
            foldout.AddToClassList("tool-help");

            Label body = new Label(WorldBuilderLocalization.Get(helpKey));
            body.AddToClassList("tool-help-text");
            body.style.whiteSpace = WhiteSpace.Normal;
            foldout.Add(body);

            container.Add(foldout);
            return container;
        }
    }
}
