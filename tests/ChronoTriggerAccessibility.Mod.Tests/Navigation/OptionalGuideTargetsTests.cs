using System.Reflection;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OptionalGuideTargetsTests
{
    // Read from the installed scripts: the actor's own load opcode supplies the class and
    // visual, its first dialogue or shop opcode supplies the identity. Evidence is in
    // artifacts/research/story-discovery-0312.
    public static TheoryData<int, int, int, int, string> Verified => new()
    {
        { 5, 14, 4, 0, "Melchior the swordsmith" },
        { 5, 18, 4, 83, "Tent of Horrors barker" },
        { 5, 25, 4, 22, "Strength game attendant" },
        { 439, 12, 4, 85, "Girl who lost her kitten" },
        { 439, 30, 4, 89, "Silver Point exchange" },
        { 6, 8, 5, 146, "Gato" },
        { 12, 17, 4, 183, "Sealed box" },
        { 17, 9, 4, 41, "Fritz" },
    };

    [Theory]
    [MemberData(nameof(Verified))]
    public void NamesTheVerifiedOptionalActors(int scene, int index, int classTag, int visual, string expected) =>
        Assert.Equal(expected, OptionalGuideTargets.ActorLabel(scene, Actor(index, classTag, visual)));

    [Fact]
    public void LeavesEveryOtherActorToItsAppearanceLabel()
    {
        // A different scene, a different slot, a different class and a different visual each
        // have to miss, so the table can never rename an unrelated actor.
        Assert.Null(OptionalGuideTargets.ActorLabel(9, Actor(14, 4, 0)));
        Assert.Null(OptionalGuideTargets.ActorLabel(5, Actor(15, 4, 0)));
        Assert.Null(OptionalGuideTargets.ActorLabel(5, Actor(14, 5, 0)));
        Assert.Null(OptionalGuideTargets.ActorLabel(5, Actor(14, 4, 1)));
    }

    [Fact]
    public void NeverDisplacesAnActorTheShippedProvidersAlreadyName()
    {
        // OpeningStoryTargets owns these two fair slots; a second name for either would make
        // the label depend on the order the providers are consulted.
        Assert.Null(OptionalGuideTargets.ActorLabel(5, Actor(17, 4, 0x50)));
        Assert.Null(OptionalGuideTargets.ActorLabel(439, Actor(15, 4, 0x63)));
        Assert.Null(OptionalGuideTargets.ActorLabel(8, Actor(13, 3, 2)));
    }

    [Fact]
    public void EveryLabelIsDistinctWithinAScene()
    {
        var byScene = new Dictionary<int, HashSet<string>>();
        foreach (var (scene, index, classTag, visual) in Candidates())
        {
            var label = OptionalGuideTargets.ActorLabel(scene, Actor(index, classTag, visual));
            if (label is null) continue;
            Assert.False(string.IsNullOrWhiteSpace(label));
            if (!byScene.TryGetValue(scene, out var labels)) byScene[scene] = labels = [];
            Assert.True(labels.Add(label), $"Scene {scene} reuses the label '{label}'.");
        }
        Assert.NotEmpty(byScene);
    }

    [Fact]
    public void SuppliesLabelsOnlyAndNoTargetsOrCoordinates()
    {
        // The helper must stay a naming table: anything that builds targets or picks a
        // category belongs to the builders root owns.
        var methods = typeof(OptionalGuideTargets)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.All(methods, method => Assert.Equal(typeof(string), method.ReturnType));
    }

    private static IEnumerable<(int Scene, int Index, int Class, int Visual)> Candidates()
    {
        foreach (var scene in new[] { 5, 6, 7, 12, 13, 14, 17, 439 })
        foreach (var index in Enumerable.Range(0, 40))
        foreach (var classTag in new[] { 3, 4, 5 })
        foreach (var visual in Enumerable.Range(0, 256))
            yield return (scene, index, classTag, visual);
    }

    private static FieldActorSnapshot Actor(int index, int classTag, int visual) =>
        new(index, 0, 0, 0, 0, 0, 0, 0, 1, 1, visual, classTag, 0, 1, 0, false, true, true, true);
}
