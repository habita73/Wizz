using Wizz.Application.Abstractions;
using Wizz.Application.AI.Routing;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات خوارزمية التوجيه التلقائي (Phase 6):
/// ترتيب الأولوية، حساب الدرجات، استبعاد المسارات غير المتاحة، وسجل القرارات.
/// </summary>
public sealed class RouteRoutingTests
{
    // ---------- AutoRoutePriority ----------

    [Fact]
    public void DefaultPriority_IsBuiltInThenLocalThenCloud()
    {
        var priority = AutoRoutePriority.Default;

        Assert.Equal(
            new[] { RouteType.BuiltIn, RouteType.Local, RouteType.Cloud },
            priority.Order);
        Assert.True(priority.IsDefault);
    }

    [Fact]
    public void PriorityPoints_DecreaseByUnitPerPosition()
    {
        var priority = AutoRoutePriority.Default;

        Assert.Equal(3 * AutoRoutePriority.PriorityUnit, priority.PriorityPoints(RouteType.BuiltIn));
        Assert.Equal(2 * AutoRoutePriority.PriorityUnit, priority.PriorityPoints(RouteType.Local));
        Assert.Equal(AutoRoutePriority.PriorityUnit, priority.PriorityPoints(RouteType.Cloud));
    }

    [Fact]
    public void Moved_ReturnsNewInstanceWithoutChangingOriginal()
    {
        var original = AutoRoutePriority.Default;

        var moved = original.Moved(RouteType.Cloud, 0);

        Assert.Equal(RouteType.Cloud, moved.Order[0]);
        Assert.Equal(RouteType.BuiltIn, moved.Order[1]);
        Assert.Equal(RouteType.Local, moved.Order[2]);

        // الأصل لم يتغير
        Assert.Equal(RouteType.BuiltIn, original.Order[0]);
    }

    [Fact]
    public void MoveUpAndMoveDown_StayWithinBounds()
    {
        var priority = AutoRoutePriority.Default;

        // الأول لا يصعد أكثر
        Assert.Same(priority, priority.MoveUp(RouteType.BuiltIn));

        // الأخير لا ينزل أكثر
        Assert.Same(priority, priority.MoveDown(RouteType.Cloud));

        var up = priority.MoveUp(RouteType.Cloud);
        Assert.Equal(new[] { RouteType.BuiltIn, RouteType.Cloud, RouteType.Local }, up.Order);

        var down = priority.MoveDown(RouteType.BuiltIn);
        Assert.Equal(new[] { RouteType.Local, RouteType.BuiltIn, RouteType.Cloud }, down.Order);
    }

    [Fact]
    public void SetOrder_DropsDuplicatesAndUnknownRoutes_AndFillsMissing()
    {
        var priority = AutoRoutePriority.Default;

        priority.SetOrder(new[] { RouteType.Local, RouteType.Local, RouteType.None });

        Assert.Equal(
            new[] { RouteType.Local, RouteType.BuiltIn, RouteType.Cloud },
            priority.Order);
        Assert.False(priority.IsDefault);
    }

    [Fact]
    public void SetOrder_MutatesSameInstance_SoRouterSeesTheChange()
    {
        var priority = AutoRoutePriority.Default;
        var same = priority;

        priority.SetOrder(new[] { RouteType.Cloud, RouteType.Local, RouteType.BuiltIn });

        Assert.Equal(RouteType.Cloud, same.Order[0]);
    }

    // ---------- RouteScorer ----------

    private static RouteAvailability Available(
        bool builtIn = false,
        bool local = false,
        DeviceCapabilityTier tier = DeviceCapabilityTier.Light,
        bool internet = true,
        bool cloudEnabled = false,
        bool cloud = false,
        bool quotaExceeded = false,
        bool analytical = false,
        bool protect = false)
        => new(builtIn, local, tier, internet, cloudEnabled, cloud, quotaExceeded, analytical, protect);

    [Fact]
    public void Choose_DefaultOrder_PrefersBuiltInWhenItCanAnswer()
    {
        var decision = RouteScorer.Choose(
            Available(builtIn: true, local: true, cloudEnabled: true, cloud: true));

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        Assert.Equal(3, decision.Candidates.Count);
        Assert.All(decision.Candidates, c => Assert.True(c.Eligible));
    }

    [Fact]
    public void Choose_DefaultOrder_PrefersLocalOverCloud_WhenBuiltInCannotAnswer()
    {
        var decision = RouteScorer.Choose(
            Available(builtIn: false, local: true, cloudEnabled: true, cloud: true));

        Assert.Equal(RouteType.Local, decision.Chosen);
    }

    [Fact]
    public void Choose_UserPutsLocalFirst_LocalBeatsBuiltIn()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);

        var decision = RouteScorer.Choose(
            Available(builtIn: true, local: true),
            priority);

        Assert.Equal(RouteType.Local, decision.Chosen);
        Assert.Contains("محلي", decision.Reason);
    }

    [Fact]
    public void Choose_UserPutsCloudFirst_CloudBeatsBuiltIn_WhenAnalytical()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Cloud, 0);

        var decision = RouteScorer.Choose(
            Available(builtIn: true, local: false, cloudEnabled: true, cloud: true, analytical: true),
            priority);

        Assert.Equal(RouteType.Cloud, decision.Chosen);
    }

    [Fact]
    public void Choose_UnavailableFirstRoute_IsSkipped()
    {
        // المستخدم يفضّل المحلي، لكن Ollama غير مشغّل → يرجع للمدمج المتاح
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);

        var decision = RouteScorer.Choose(Available(builtIn: true, local: false), priority);

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        var local = decision.Candidates.Single(c => c.Route == RouteType.Local);
        Assert.False(local.Eligible);
        Assert.Equal(0, local.Score);
    }

    [Fact]
    public void Choose_IneligibleRoutesNeverWinEvenWithTopPriority()
    {
        var priority = new AutoRoutePriority(new[] { RouteType.Cloud, RouteType.Local, RouteType.BuiltIn });

        var decision = RouteScorer.Choose(Available(builtIn: true), priority);

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        Assert.Equal(RouteType.BuiltIn, decision.Candidates.First(c => c.Eligible).Route);
    }

    [Fact]
    public void Choose_CloudEligibility_ExplainsEachBlocker()
    {
        var disabled = RouteScorer.Choose(Available(cloudEnabled: false));
        var offline = RouteScorer.Choose(Available(cloudEnabled: true, internet: false));
        var quota = RouteScorer.Choose(Available(cloudEnabled: true, cloud: false, quotaExceeded: true));

        Assert.Contains("غير مفعّلة", disabled.Candidates.Single(c => c.Route == RouteType.Cloud).Reason);
        Assert.Contains("الإنترنت", offline.Candidates.Single(c => c.Route == RouteType.Cloud).Reason);
        Assert.Contains("الحصة", quota.Candidates.Single(c => c.Route == RouteType.Cloud).Reason);
    }

    [Fact]
    public void Choose_NoEligibleRoute_ReturnsNoneWithReason()
    {
        var decision = RouteScorer.Choose(Available());

        Assert.Equal(RouteType.None, decision.Chosen);
        Assert.DoesNotContain(decision.Candidates, c => c.Eligible);
    }

    [Fact]
    public void Choose_OrdersCandidates_EligibleFirstThenScoreThenPriority()
    {
        var decision = RouteScorer.Choose(
            Available(builtIn: false, local: true, cloudEnabled: true, cloud: true));

        Assert.Equal(RouteType.Local, decision.Candidates[0].Route);
        Assert.Equal(RouteType.Cloud, decision.Candidates[1].Route);
        Assert.Equal(RouteType.BuiltIn, decision.Candidates[2].Route);
    }

    [Fact]
    public void Choose_MediumAndHeavyDevices_RaiseLocalQuality()
    {
        var light = RouteScorer.Choose(Available(local: true, tier: DeviceCapabilityTier.Light));
        var heavy = RouteScorer.Choose(Available(local: true, tier: DeviceCapabilityTier.Heavy));

        var lightScore = light.Candidates.Single(c => c.Route == RouteType.Local).Score;
        var heavyScore = heavy.Candidates.Single(c => c.Route == RouteType.Local).Score;

        Assert.True(heavyScore > lightScore);
    }

    [Fact]
    public void Choose_ProtectBuiltIn_KeepsBuiltInEvenWhenLocalIsFirst()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);

        var decision = RouteScorer.Choose(
            Available(builtIn: true, local: true, protect: true),
            priority);

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        Assert.Equal(RouteScorer.ProtectedReason, decision.Reason);
    }

    [Fact]
    public void Choose_ProtectBuiltIn_HasNoEffectWhenBuiltInCannotAnswer()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);

        var decision = RouteScorer.Choose(
            Available(builtIn: false, local: true, protect: true),
            priority);

        Assert.Equal(RouteType.Local, decision.Chosen);
    }

    [Fact]
    public void BuiltInProtected_MarksLocalAndCloudUnchecked()
    {
        var decision = RouteScorer.BuiltInProtected();

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        Assert.Equal(RouteScorer.ProtectedReason, decision.Reason);
        Assert.All(decision.Candidates.Where(c => c.Route != RouteType.BuiltIn), c =>
        {
            Assert.False(c.Checked);
            Assert.Contains("حماية أسئلة الأرقام", c.Reason);
        });
    }

    [Fact]
    public void Manual_Decision_HasNoCandidatesAndManualReason()
    {
        var manual = RouteDecision.Manual(RouteType.Local);

        Assert.Equal(RouteType.Local, manual.Chosen);
        Assert.Empty(manual.Candidates);
        Assert.Contains("يدوي", manual.Reason);
    }

    [Fact]
    public void BuiltInFirst_ReturnsGuaranteedDecision_AndMarksOthersUnchecked()
    {
        var decision = RouteScorer.BuiltInFirst("بيانات متجرك جاهزة محلياً دون إنترنت");

        Assert.Equal(RouteType.BuiltIn, decision.Chosen);
        var builtIn = decision.Candidates.Single(c => c.Route == RouteType.BuiltIn);
        Assert.True(builtIn.Eligible);
        Assert.True(builtIn.Checked);
        Assert.All(decision.Candidates.Where(c => c.Route != RouteType.BuiltIn), c =>
        {
            Assert.False(c.Eligible);
            Assert.False(c.Checked);
            Assert.Contains("لم يُفحص", c.Reason);
        });

        // درجته تفوق أقصى درجة ممكنة لأي مسار آخر
        Assert.True(builtIn.Score > (2 * AutoRoutePriority.PriorityUnit) + 80);
    }

    // ---------- RouteDecisionLog ----------

    [Fact]
    public void DecisionLog_Recent_ReturnsNewestFirst()
    {
        var log = new RouteDecisionLog();
        for (var i = 1; i <= 3; i++)
        {
            log.Add(Entry($"سؤال {i}"));
        }

        var recent = log.Recent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("سؤال 3", recent[0].Question);
        Assert.Equal("سؤال 2", recent[1].Question);
    }

    [Fact]
    public void DecisionLog_KeepsOnlyMaxEntries()
    {
        var log = new RouteDecisionLog();
        for (var i = 0; i < RouteDecisionLog.MaxEntries + 7; i++)
        {
            log.Add(Entry($"سؤال {i}"));
        }

        Assert.Equal(RouteDecisionLog.MaxEntries, log.Count);
        Assert.Equal($"سؤال {RouteDecisionLog.MaxEntries + 6}", log.Recent(1)[0].Question);
    }

    [Fact]
    public void DecisionLog_Clear_EmptiesLog()
    {
        var log = new RouteDecisionLog();
        log.Add(Entry("سؤال"));

        log.Clear();

        Assert.Equal(0, log.Count);
        Assert.Empty(log.Recent(5));
    }

    [Fact]
    public void DecisionLog_RecentWithNonPositiveCount_ReturnsEmpty()
    {
        var log = new RouteDecisionLog();
        log.Add(Entry("سؤال"));

        Assert.Empty(log.Recent(0));
        Assert.Empty(log.Recent(-1));
    }

    private static RouteDecisionEntry Entry(string question)
        => new(DateTime.UtcNow, question, RouteType.BuiltIn, "سبب", Array.Empty<RouteCandidate>());
}
