using System;
using System.Collections.Generic;
using System.Text;

public static class JourneyTextVariationComposer
{
    private const string UnresolvedOutcomeKey = "__unresolved";

    private static readonly Dictionary<string, string[]> IntroTemplates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        {
            "default",
            new[]
            {
                "A first-person passage through hunger, war, and medical transformation. Gesture memory: {gestureSummary}.",
                "A body moved through hunger, war, and medical transformation from the inside. Gesture memory persists as: {gestureSummary}.",
                "One subjective life crossed hunger, war, and medical transformation without release. Gesture memory remains: {gestureSummary}.",
                "The journey kept its first-person witness through hunger, war, and medical transformation. Gesture memory: {gestureSummary}.",
            }
        },
        {
            "despair",
            new[]
            {
                "A first-person descent through hunger, war, and medical transformation. Gesture memory curdled into: {gestureSummary}.",
                "The journey closed around the body through hunger, war, and medical transformation. Gesture memory remains trapped in: {gestureSummary}.",
                "A life crossed hunger, war, and medical transformation beneath accumulating pressure. Gesture memory lingers as: {gestureSummary}.",
            }
        },
        {
            "transcendence",
            new[]
            {
                "A first-person crossing through hunger, war, and medical transformation toward unstable transcendence. Gesture memory echoes as: {gestureSummary}.",
                "The body survived hunger, war, and medical transformation only to become something altered. Gesture memory remains: {gestureSummary}.",
                "A subjective passage through deprivation, surrender, and transformation bent toward transcendence. Gesture memory persists as: {gestureSummary}.",
            }
        }
    };

    private static readonly Dictionary<string, string[]> RecapTemplates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { BuildSceneOutcomeKey("Test", "Scarcity"), new[]
            {
                "You stayed with deprivation until it felt structural.",
                "Scarcity did not pass through you; it settled into you.",
                "Hunger narrowed the world until collapse felt ordinary.",
                "Deprivation remained the most stable truth in the frame.",
            }
        },
        { BuildSceneOutcomeKey("Test", "Balance"), new[]
            {
                "You held a temporary balance that never stopped trembling.",
                "Stability appeared only as a narrow ledge above collapse.",
                "For a moment, restraint kept the body from falling further.",
                "Balance survived, but only as something fragile and conditional.",
            }
        },
        { BuildSceneOutcomeKey("Test", "Excess"), new[]
            {
                "Need inverted itself until excess became another wound.",
                "Abundance arrived in a form the body could not trust.",
                "Consumption kept expanding long after relief should have begun.",
                "Excess filled the frame, but never resolved the hunger beneath it.",
            }
        },
        { BuildSceneOutcomeKey("2", "Submission"), new[]
            {
                "Fear compressed the body into immediate obedience.",
                "Submission arrived before certainty could defend itself.",
                "Command overtook the body faster than resistance could form.",
                "The checkpoint reduced survival to a posture of surrender.",
            }
        },
        { BuildSceneOutcomeKey("2", "Hesitation"), new[]
            {
                "You were held between compliance and refusal long enough for both to bruise the body.",
                "Hesitation became its own form of captivity.",
                "The war scene trapped action inside doubt and unfinished motion.",
                "You lingered in the suspended space between fear and decision.",
            }
        },
        { BuildSceneOutcomeKey("2", "Resistance"), new[]
            {
                "You resisted the order even while the threat remained close.",
                "Refusal held for a moment longer than fear wanted it to.",
                "Resistance survived inside a body already under pressure.",
                "You kept a guarded defiance where surrender was expected.",
            }
        },
        { BuildSceneOutcomeKey("3", "Despair"), new[]
            {
                "The chamber answered the body with silence and delay.",
                "No relief reached you before despair finished closing in.",
                "The plea broke against a room that refused to answer.",
                "Helplessness remained longer than faith could survive it.",
            }
        },
        { BuildSceneOutcomeKey("3", "Supplication"), new[]
            {
                "You answered collapse with prayer before certainty returned.",
                "Appeal and vulnerability became the only language left to the body.",
                "Supplication held the room together for one more breath.",
                "You met collapse by turning toward prayer instead of control.",
            }
        },
        { BuildSceneOutcomeKey("3", "Apotheosis"), new[]
            {
                "Healing crossed its threshold and became synthetic transcendence.",
                "The body moved beyond recovery into something post-human.",
                "Transformation continued until prayer was replaced by machinery.",
                "You passed through treatment into a colder form of ascent.",
            }
        },
        { BuildSceneOutcomeKey("Test", UnresolvedOutcomeKey), new[]
            {
                "The hunger chapter remained unresolved, still tightening at the edges.",
                "No final shape emerged from the hunger chapter.",
                "The first chapter ended without fully surrendering its question.",
            }
        },
        { BuildSceneOutcomeKey("2", UnresolvedOutcomeKey), new[]
            {
                "The war chapter remained unresolved beneath suspended threat.",
                "No clear answer survived the checkpoint.",
                "The war scene closed without releasing its pressure.",
            }
        },
        { BuildSceneOutcomeKey("3", UnresolvedOutcomeKey), new[]
            {
                "The medical chapter remained unresolved inside ritual and dread.",
                "No stable answer emerged from the chamber.",
                "The final chapter held its breath without resolution.",
            }
        },
    };

    private static readonly Dictionary<string, string[]> FinalEndingTemplates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        {
            "EchoOfHunger",
            new[]
            {
                "The future remained shaped by deprivation long after the body learned its pattern.",
                "Hunger outlived every transition and returned as the final law of the journey.",
                "Deprivation survived the whole passage and named the ending for you.",
                "What remained at the end was not relief, but the echo of hunger made permanent.",
            }
        },
        {
            "ComfortCollapse",
            new[]
            {
                "Comfort expanded until it became indistinguishable from ruin.",
                "Relief lost its boundary and collapsed into another form of decay.",
                "What promised safety finally revealed itself as collapse in softer clothing.",
                "The ending arrived as comfort overgrown into a structure of failure.",
            }
        },
        {
            "SyntheticGodhood",
            new[]
            {
                "Technology replaced prayer and remade the body into its own doctrine.",
                "The human frame was rewritten until transcendence felt engineered rather than divine.",
                "The ending crossed beyond healing into a colder synthetic godhood.",
                "What remained was not salvation, but a technological ascent that displaced the human body.",
            }
        },
        {
            "__default",
            new[]
            {
                "The journey reached an ending that refused to become ordinary.",
                "What remained at the end resisted a simple name.",
                "The final state arrived, but would not flatten into certainty.",
            }
        }
    };

    public static string BuildJourneyIdentityLine(JourneySessionManager session)
    {
        string gestureSummary = BuildGestureSummary(session != null ? session.GestureSemanticRecords : null);
        string moodKey = ResolveIntroMoodKey(session);
        int seed = BuildBaseSeed(session, string.Empty);
        string template = SelectVariant(GetTemplates(IntroTemplates, moodKey, "default"), CombineSeed(seed, "intro"));
        return ReplaceSlots(template, CombineSeed(seed, "intro-slots"), gestureSummary);
    }

    public static string BuildRecapSentence(string sceneName, SceneOutcomeRecord outcome, JourneySessionManager session)
    {
        string outcomeKey = outcome != null && !string.IsNullOrWhiteSpace(outcome.outcomeKey) ? outcome.outcomeKey : UnresolvedOutcomeKey;
        string catalogKey = BuildSceneOutcomeKey(sceneName, outcomeKey);
        string[] templates = GetTemplates(RecapTemplates, catalogKey, BuildSceneOutcomeKey(sceneName, UnresolvedOutcomeKey));
        int seed = BuildBaseSeed(session, sceneName);
        string template = SelectVariant(templates, CombineSeed(seed, $"recap-{sceneName}-{outcomeKey}"));
        return ReplaceSlots(template, CombineSeed(seed, $"recap-slots-{sceneName}-{outcomeKey}"), string.Empty);
    }

    public static string BuildFinalEndingSentence(string finalEndingKey, string fallbackSentence, JourneySessionManager session)
    {
        string[] templates = GetTemplates(FinalEndingTemplates, finalEndingKey, "__default");
        int seed = BuildBaseSeed(session, finalEndingKey);
        string template = SelectVariant(templates, CombineSeed(seed, $"final-{finalEndingKey}"));
        string resolved = ReplaceSlots(template, CombineSeed(seed, $"final-slots-{finalEndingKey}"), string.Empty);
        return string.IsNullOrWhiteSpace(resolved) ? fallbackSentence : resolved;
    }

    private static string ResolveIntroMoodKey(JourneySessionManager session)
    {
        if (session == null)
        {
            return "default";
        }

        SceneOutcomeRecord medical = session.GetSceneOutcome("3");
        if (medical != null)
        {
            if (string.Equals(medical.outcomeKey, "Apotheosis", StringComparison.OrdinalIgnoreCase))
            {
                return "transcendence";
            }

            if (string.Equals(medical.outcomeKey, "Despair", StringComparison.OrdinalIgnoreCase))
            {
                return "despair";
            }
        }

        return "default";
    }

    private static int BuildBaseSeed(JourneySessionManager session, string layerKey)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (session != null ? session.JourneyId : 0);
            hash = hash * 31 + GetOutcomeHash(session, "Test");
            hash = hash * 31 + GetOutcomeHash(session, "2");
            hash = hash * 31 + GetOutcomeHash(session, "3");
            hash = hash * 31 + GetGestureSummaryHash(session != null ? session.GestureSemanticRecords : null);
            hash = hash * 31 + (layerKey != null ? layerKey.GetHashCode() : 0);
            return hash;
        }
    }

    private static int GetOutcomeHash(JourneySessionManager session, string sceneName)
    {
        SceneOutcomeRecord outcome = session != null ? session.GetSceneOutcome(sceneName) : null;
        return outcome != null && !string.IsNullOrWhiteSpace(outcome.outcomeKey)
            ? outcome.outcomeKey.GetHashCode()
            : 0;
    }

    private static int GetGestureSummaryHash(IReadOnlyList<GestureSemanticRecord> records)
    {
        if (records == null || records.Count == 0)
        {
            return 0;
        }

        unchecked
        {
            int hash = 23;
            int count = Math.Min(records.Count, 8);
            for (int i = 0; i < count; i++)
            {
                GestureSemanticRecord record = records[i];
                hash = hash * 31 + (record != null && !string.IsNullOrWhiteSpace(record.gestureKey)
                    ? record.gestureKey.GetHashCode()
                    : 0);
                hash = hash * 31 + (record != null && !string.IsNullOrWhiteSpace(record.detail)
                    ? record.detail.GetHashCode()
                    : 0);
            }

            return hash;
        }
    }

    private static string BuildGestureSummary(IReadOnlyList<GestureSemanticRecord> gestureRecords)
    {
        if (gestureRecords == null || gestureRecords.Count == 0)
        {
            return "no gestures were captured";
        }

        List<string> summaries = new List<string>();
        int count = Math.Min(gestureRecords.Count, 6);
        for (int i = 0; i < count; i++)
        {
            GestureSemanticRecord record = gestureRecords[i];
            if (record == null || string.IsNullOrWhiteSpace(record.gestureKey))
            {
                continue;
            }

            summaries.Add(record.gestureKey.Replace('_', ' '));
        }

        return summaries.Count > 0 ? string.Join(", ", summaries) : "no gestures were captured";
    }

    private static string[] GetTemplates(Dictionary<string, string[]> source, string key, string fallbackKey)
    {
        if (!string.IsNullOrWhiteSpace(key) && source.TryGetValue(key, out string[] direct) && direct != null && direct.Length > 0)
        {
            return direct;
        }

        if (!string.IsNullOrWhiteSpace(fallbackKey) && source.TryGetValue(fallbackKey, out string[] fallback) && fallback != null && fallback.Length > 0)
        {
            return fallback;
        }

        return new[] { string.Empty };
    }

    private static string SelectVariant(string[] variants, int seed)
    {
        if (variants == null || variants.Length == 0)
        {
            return string.Empty;
        }

        int index = Math.Abs(seed % variants.Length);
        return variants[index];
    }

    private static int CombineSeed(int baseSeed, string salt)
    {
        unchecked
        {
            return (baseSeed * 397) ^ (salt != null ? salt.GetHashCode() : 0);
        }
    }

    private static string ReplaceSlots(string template, int seed, string gestureSummary)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        string tactile = SelectVariant(new[]
        {
            "tactile",
            "bodily",
            "oppressive",
            "airless",
        }, CombineSeed(seed, "tactile"));

        string pressure = SelectVariant(new[]
        {
            "pressure",
            "weight",
            "strain",
            "drag",
        }, CombineSeed(seed, "pressure"));

        StringBuilder builder = new StringBuilder(template);
        builder.Replace("{gestureSummary}", string.IsNullOrWhiteSpace(gestureSummary) ? "no gestures were captured" : gestureSummary);
        builder.Replace("{tactile}", tactile);
        builder.Replace("{pressure}", pressure);
        return builder.ToString();
    }

    private static string BuildSceneOutcomeKey(string sceneName, string outcomeKey)
    {
        return string.Concat(sceneName ?? string.Empty, "::", outcomeKey ?? string.Empty);
    }
}
