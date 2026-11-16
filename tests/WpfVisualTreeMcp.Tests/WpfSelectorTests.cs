using FluentAssertions;
using WpfVisualTreeMcp.Server.Controls;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public sealed class WpfSelectorTests
{
    [Fact]
    public async Task SkipsUnavailableValuesAndMatchesTypedIndex()
    {
        var result = await WpfSelector.FindAsync(
            () => Task.FromResult(new[] { 1, 2, 3 }),
            item => Task.FromResult(new SelectorValue<int>(item != 2, item)),
            3, CancellationToken.None, "index");

        result.Should().Be(3);
    }

    [Fact]
    public async Task RejectsAmbiguousMatches()
    {
        var action = () => WpfSelector.FindAsync(
            () => Task.FromResult(new[] { 1, 2 }),
            _ => Task.FromResult(new SelectorValue<string>(true, "same")),
            "same", CancellationToken.None, "content");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ambiguous*");
    }

    [Fact]
    public async Task ReacquiresAllCandidatesAfterAStaleHandle()
    {
        var searches = 0;
        var result = await WpfSelector.FindAsync(
            () => Task.FromResult(++searches == 1 ? new[] { 1, 2 } : new[] { 3 }),
            item => item == 2 ? throw new InvalidOperationException("Element handle 'stale' not found")
                : Task.FromResult(new SelectorValue<string>(true, "match")),
            "match", CancellationToken.None, "content");

        result.Should().Be(3);
        searches.Should().Be(2);
    }

    [Fact]
    public async Task PropagatesOtherReadErrors()
    {
        var action = () => WpfSelector.FindAsync<int, string>(
            () => Task.FromResult(new[] { 1 }),
            _ => throw new InvalidOperationException("read failed"),
            "match", CancellationToken.None, "content");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("read failed");
    }

    [Fact]
    public async Task ReportsMissingWhenNoValueMatches()
    {
        var action = () => WpfSelector.FindAsync(
            () => Task.FromResult(new[] { 1 }),
            _ => Task.FromResult(new SelectorValue<string>(false, "match")),
            "match", CancellationToken.None, "content", TimeSpan.Zero);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Missing*");
    }
}
