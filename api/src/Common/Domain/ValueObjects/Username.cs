namespace Theodo.DotnetBoilerplate.Common.Domain.ValueObjects;

public sealed record Username
{
    public string Value { get;  }

    public Username(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 50)
            throw new ArgumentException("Username must be 1-50 non-blank characters", nameof(value));
        Value = value;
    }
}