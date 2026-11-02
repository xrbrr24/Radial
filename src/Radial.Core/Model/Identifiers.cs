namespace Radial.Core.Model;

/// <summary>Identificador estable de un menú. No depende del nombre visible.</summary>
public readonly record struct MenuId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identificador estable de un elemento. Es único en toda la configuración.</summary>
public readonly record struct ItemId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}
