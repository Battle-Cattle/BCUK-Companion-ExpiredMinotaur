using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill.UI;

public sealed class TreadmillActionEditorTests
{
    [SkippableFact]
    public void StartsAtOneKmhAndBuildsANudge()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new TreadmillActionEditor();

            var action = Assert.IsType<TreadmillNudgeSpeedAction>(editor.TryBuild(out var error));
            Assert.Null(error);
            Assert.Equal(1.0, action.DeltaKmh);
            Assert.Equal("Treadmill", editor.DisplayName);
        });
    }

    [SkippableFact]
    public void LoadThenTryBuild_RoundTripsTheDelta()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new TreadmillActionEditor();
            var original = new TreadmillNudgeSpeedAction { DeltaKmh = -0.3 };

            Assert.True(editor.CanEdit(original));
            Assert.False(editor.CanEdit(new DelayAction()));
            editor.Load(original);

            Assert.Equal(-0.3, Assert.IsType<TreadmillNudgeSpeedAction>(editor.TryBuild(out _)).DeltaKmh);
        });
    }

    [SkippableTheory]
    [InlineData("fast")]
    [InlineData("")]
    [InlineData("Infinity")]
    public void TryBuild_RejectsANonNumericDelta(string text)
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new TreadmillActionEditor();
            WpfTest.All<TextBox>(editor.View).Single().Text = text;

            Assert.Null(editor.TryBuild(out var error));
            Assert.Equal("Enter a numeric speed delta.", error);
        });
    }
}
