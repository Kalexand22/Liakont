namespace Stratum.Common.Infrastructure.Tests.Unit.Gis;

using FluentAssertions;
using Stratum.Common.Infrastructure.Gis;
using Xunit;

public sealed class GeoJsonServiceTests
{
    private readonly GeoJsonService _sut = new();

    [Fact]
    public void Parse_Valid_Point_Returns_GeoJsonGeometry()
    {
        var json = """{"type":"Point","coordinates":[2.3522,48.8566]}""";

        var result = _sut.Parse(json);

        result.Type.Should().Be("Point");
        result.GeoJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_Valid_Polygon_Returns_GeoJsonGeometry()
    {
        var json = """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""";

        var result = _sut.Parse(json);

        result.Type.Should().Be("Polygon");
    }

    [Fact]
    public void Parse_Valid_LineString_Returns_GeoJsonGeometry()
    {
        var json = """{"type":"LineString","coordinates":[[0,0],[10,10]]}""";

        var result = _sut.Parse(json);

        result.Type.Should().Be("LineString");
    }

    [Fact]
    public void Parse_Invalid_Json_Throws_FormatException()
    {
        var act = () => _sut.Parse("not valid json");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_Null_Throws_ArgumentException()
    {
        var act = () => _sut.Parse(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_Empty_Throws_ArgumentException()
    {
        var act = () => _sut.Parse(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_Valid_Polygon_Returns_True()
    {
        var geom = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");

        _sut.Validate(geom).Should().BeTrue();
    }

    [Fact]
    public void Validate_Bowtie_Polygon_Returns_False()
    {
        var geom = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,10],[10,0],[0,10],[0,0]]]}""");

        _sut.Validate(geom).Should().BeFalse();
    }

    [Fact]
    public void CalculateBoundingBox_Returns_Correct_Envelope()
    {
        var geom = _sut.Parse("""{"type":"Polygon","coordinates":[[[1,2],[5,2],[5,8],[1,8],[1,2]]]}""");

        var bbox = _sut.CalculateBoundingBox(geom);

        bbox.MinX.Should().Be(1);
        bbox.MinY.Should().Be(2);
        bbox.MaxX.Should().Be(5);
        bbox.MaxY.Should().Be(8);
    }

    [Fact]
    public void Intersects_Overlapping_Polygons_Returns_True()
    {
        var poly1 = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var poly2 = _sut.Parse("""{"type":"Polygon","coordinates":[[[5,5],[15,5],[15,15],[5,15],[5,5]]]}""");

        _sut.Intersects(poly1, poly2).Should().BeTrue();
    }

    [Fact]
    public void Intersects_Disjoint_Polygons_Returns_False()
    {
        var poly1 = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]}""");
        var poly2 = _sut.Parse("""{"type":"Polygon","coordinates":[[[10,10],[11,10],[11,11],[10,11],[10,10]]]}""");

        _sut.Intersects(poly1, poly2).Should().BeFalse();
    }

    [Fact]
    public void Contains_Point_Inside_Polygon_Returns_True()
    {
        var polygon = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var point = _sut.Parse("""{"type":"Point","coordinates":[5,5]}""");

        _sut.Contains(polygon, point).Should().BeTrue();
    }

    [Fact]
    public void Contains_Point_Outside_Polygon_Returns_False()
    {
        var polygon = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var point = _sut.Parse("""{"type":"Point","coordinates":[20,20]}""");

        _sut.Contains(polygon, point).Should().BeFalse();
    }

    [Fact]
    public void Area_Square_Polygon_Returns_Correct_Value()
    {
        var polygon = _sut.Parse("""{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");

        _sut.Area(polygon).Should().Be(100);
    }

    [Fact]
    public void Area_Point_Returns_Zero()
    {
        var point = _sut.Parse("""{"type":"Point","coordinates":[5,5]}""");

        _sut.Area(point).Should().Be(0);
    }

    [Fact]
    public void Area_LineString_Returns_Zero()
    {
        var line = _sut.Parse("""{"type":"LineString","coordinates":[[0,0],[10,10]]}""");

        _sut.Area(line).Should().Be(0);
    }

    [Fact]
    public void Serialize_Returns_Original_GeoJson()
    {
        var json = """{"type":"Point","coordinates":[2.3522,48.8566]}""";
        var geom = _sut.Parse(json);

        var result = _sut.Serialize(geom);

        result.Should().Be(json);
    }
}
