namespace SoapAndSoul.Domain.Catalog;

public static class Units
{
    /// <summary>Drops are bought by volume: 1 мл = 20 крап.</summary>
    public const int DropsPerMilliliter = 20;

    public static string Label(this MeasureUnit unit) => unit switch
    {
        MeasureUnit.Piece => "шт",
        MeasureUnit.Gram => "г",
        MeasureUnit.Milliliter => "мл",
        MeasureUnit.Drop => "крап",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The unit a purchase is measured in (drops are bought in millilitres).</summary>
    public static MeasureUnit PurchaseUnit(this MeasureUnit unit) =>
        unit == MeasureUnit.Drop ? MeasureUnit.Milliliter : unit;

    /// <summary>Step used by the +/− buttons for an amount in this unit.</summary>
    public static decimal Step(this MeasureUnit unit) =>
        unit is MeasureUnit.Gram or MeasureUnit.Milliliter ? 5 : 1;
}
