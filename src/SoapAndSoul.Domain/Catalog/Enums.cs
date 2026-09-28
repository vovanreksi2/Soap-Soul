using System.Text.Json.Serialization;

namespace SoapAndSoul.Domain.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<CosmeticLine>))]
public enum CosmeticLine
{
    Soap,
    Perfume,
}

[JsonConverter(typeof(JsonStringEnumConverter<MeasureUnit>))]
public enum MeasureUnit
{
    Piece,
    Gram,
    Milliliter,
    Drop,
}

[JsonConverter(typeof(JsonStringEnumConverter<CategoryKey>))]
public enum CategoryKey
{
    // Soap
    Mold,
    SoapBase,
    Pigment,
    Extract,
    Fragrance,
    EssentialOil,
    Packaging,
    Tools,

    // Perfume (Fragrance is shared with soap)
    Bottle,
    PerfumeBase,
}
