namespace Stratum.Common.Infrastructure.Tests.Unit.Gis;

using FluentAssertions;
using Stratum.Common.Infrastructure.Gis;
using Xunit;

public sealed class SpatialConflictDetectorTests
{
    private readonly GeoJsonService _geoJsonService = new();
    private readonly SpatialConflictDetector _sut = new();

    [Fact]
    public void DetectOverlaps_Empty_Existing_Returns_Empty()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");

        var result = _sut.DetectOverlaps(candidate, []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectOverlaps_No_Intersection_Returns_Empty()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]}""");
        var existing = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[10,10],[11,10],[11,11],[10,11],[10,10]]]}""");

        var result = _sut.DetectOverlaps(candidate, [existing]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectOverlaps_One_Intersection_Returns_One_Conflict()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var existing = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[5,5],[15,5],[15,15],[5,15],[5,5]]]}""");

        var result = _sut.DetectOverlaps(candidate, [existing]);

        result.Should().ContainSingle();
        result[0].ExistingGeometryIndex.Should().Be(0);
        result[0].OverlapAreaSquareMeters.Should().Be(25); // 5x5 overlap
        result[0].OverlapGeometry.Should().NotBeNull();
    }

    [Fact]
    public void DetectOverlaps_Multiple_Existing_Returns_Only_Conflicting()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var noOverlap = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[20,20],[21,20],[21,21],[20,21],[20,20]]]}""");
        var overlapping = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[5,5],[15,5],[15,15],[5,15],[5,5]]]}""");

        var result = _sut.DetectOverlaps(candidate, [noOverlap, overlapping]);

        result.Should().ContainSingle();
        result[0].ExistingGeometryIndex.Should().Be(1);
    }

    [Fact]
    public void DetectOverlaps_Point_In_Polygon_Returns_Conflict()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Point","coordinates":[5,5]}""");
        var existing = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");

        var result = _sut.DetectOverlaps(candidate, [existing]);

        result.Should().ContainSingle();
        result[0].OverlapAreaSquareMeters.Should().Be(0); // Point has no area
    }

    [Fact]
    public void DetectOverlaps_All_Overlap_Returns_All()
    {
        var candidate = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}""");
        var existing1 = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[1,1],[5,1],[5,5],[1,5],[1,1]]]}""");
        var existing2 = _geoJsonService.Parse(
            """{"type":"Polygon","coordinates":[[[6,6],[9,6],[9,9],[6,9],[6,6]]]}""");

        var result = _sut.DetectOverlaps(candidate, [existing1, existing2]);

        result.Should().HaveCount(2);
        result[0].ExistingGeometryIndex.Should().Be(0);
        result[1].ExistingGeometryIndex.Should().Be(1);
    }
}
