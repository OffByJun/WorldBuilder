namespace WorldBuilder.Editor
{
    public interface IUndoable
    {
        void RecordUndo(string label);
    }
}
