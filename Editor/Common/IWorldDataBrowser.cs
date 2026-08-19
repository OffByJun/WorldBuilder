using System;

namespace WorldBuilder.Editor
{
    public interface IWorldDataBrowser
    {
        void FilterByType(Type entryType);
        void SortBy(Func<IWorldDataEntry, IComparable> keySelector);
    }
}
