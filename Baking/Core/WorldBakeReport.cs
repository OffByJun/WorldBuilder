using System;
using System.Collections.Generic;
using System.Text;

namespace WorldBuilder.Baking.Core
{
    public enum BakeIssueSeverity { Info, Warning, Error }

    public readonly struct BakeIssue : IComparable<BakeIssue>
    {
        public readonly BakeIssueSeverity Severity;
        public readonly string Code;
        public readonly string Path;
        public readonly string Message;

        public BakeIssue(BakeIssueSeverity severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public int CompareTo(BakeIssue other)
        {
            int severity = Severity.CompareTo(other.Severity);
            if (severity != 0) return severity;
            int code = string.CompareOrdinal(Code, other.Code);
            if (code != 0) return code;
            int path = string.CompareOrdinal(Path, other.Path);
            return path != 0 ? path : string.CompareOrdinal(Message, other.Message);
        }
    }

    public sealed class WorldBakeReport
    {
        private readonly List<BakeIssue> issues = new List<BakeIssue>();
        public IReadOnlyList<BakeIssue> Issues => issues;
        public bool HasErrors { get; private set; }

        public void Add(BakeIssueSeverity severity, string code, string path, string message)
        {
            issues.Add(new BakeIssue(severity, code, path, message));
            if (severity == BakeIssueSeverity.Error) HasErrors = true;
        }

        public void Sort() => issues.Sort();

        public void Merge(WorldBakeReport other)
        {
            if (other == null) return;
            for (int i = 0; i < other.issues.Count; i++)
            {
                BakeIssue issue = other.issues[i];
                Add(issue.Severity, issue.Code, issue.Path, issue.Message);
            }
        }

        public string BuildDeterministicText()
        {
            Sort();
            StringBuilder builder = new StringBuilder();
            foreach (BakeIssue issue in issues)
                builder.Append((int)issue.Severity).Append('|').Append(issue.Code).Append('|')
                    .Append(issue.Path).Append('|').Append(issue.Message).Append('\n');
            return builder.ToString();
        }
    }
}
