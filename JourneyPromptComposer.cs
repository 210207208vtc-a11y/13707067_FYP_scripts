using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class JourneyPromptComposer
{
    private const string EchoOfHunger = "EchoOfHunger";
    private const string ComfortCollapse = "ComfortCollapse";
    private const string SyntheticGodhood = "SyntheticGodhood";
    private const string NegativePromptClause =
        "Silent. No subtitles. No captions. No on-screen text. No typography. Human-scale imagery only.";

    public static EndingPromptPackage BuildEndingPromptPackage(JourneySessionManager session)
    {
        if (session == null)
        {
            return null;
        }

        StoryOutcomeCatalog catalog = session.Catalog != null ? session.Catalog : StoryOutcomeCatalog.CreateRuntimeDefault();
        SceneOutcomeRecord hunger = session.GetSceneOutcome("Test");
        SceneOutcomeRecord war = session.GetSceneOutcome("2");
        SceneOutcomeRecord medical = session.GetSceneOutcome("3");

        string finalEndingKey = ChooseEndingKey(hunger, war, medical);
        StoryOutcomeCatalog.FinalEndingDefinition finalEnding = catalog.FindFinalEnding(finalEndingKey);
        string finalEndingTitle = finalEnding != null ? finalEnding.displayTitle : "Unknown Ending";
        string fallbackFinalEndingSentence = finalEnding != null ? finalEnding.finalSentence : "The journey reached an undefined conclusion.";

        List<string> allTags = new List<string>(session.AggregatedPromptTags);
        string hungerTitle = hunger != null ? hunger.displayTitle : "Unknown Hunger State";
        string warTitle = war != null ? war.displayTitle : "Unknown War State";
        string medicalTitle = medical != null ? medical.displayTitle : "Unknown Medical State";
        string hungerSentence = ResolveRecapSentence("Test", hunger, session);
        string warSentence = ResolveRecapSentence("2", war, session);
        string medicalSentence = ResolveRecapSentence("3", medical, session);
        string journeyIdentityLine = JourneyTextVariationComposer.BuildJourneyIdentityLine(session);
        string finalEndingSentence = JourneyTextVariationComposer.BuildFinalEndingSentence(finalEndingKey, fallbackFinalEndingSentence, session);

        return new EndingPromptPackage
        {
            journeyIdentityLine = journeyIdentityLine,
            hungerRecapLine = hungerSentence,
            warRecapLine = warSentence,
            medicalRecapLine = medicalSentence,
            finalEndingKey = finalEndingKey,
            finalEndingTitle = finalEndingTitle,
            finalEndingSentence = finalEndingSentence,
            finalVideoPrompt = BuildFinalVideoPrompt(
                hungerTitle,
                hungerSentence,
                warTitle,
                warSentence,
                medicalTitle,
                medicalSentence,
                finalEndingTitle,
                finalEndingSentence,
                allTags),
            generatedAtSessionTime = Time.unscaledTime,
            aggregatedTags = allTags,
            segmentSpecs = BuildSegmentSpecs(
                session,
                hungerTitle,
                hungerSentence,
                warTitle,
                warSentence,
                medicalTitle,
                medicalSentence,
                finalEndingTitle,
                finalEndingSentence),
        };
    }

    public static string ChooseEndingKey(SceneOutcomeRecord hunger, SceneOutcomeRecord war, SceneOutcomeRecord medical)
    {
        string hungerEnding = MapOutcomeToEnding("Test", hunger != null ? hunger.outcomeKey : string.Empty);
        string warEnding = MapOutcomeToEnding("2", war != null ? war.outcomeKey : string.Empty);
        string medicalEnding = MapOutcomeToEnding("3", medical != null ? medical.outcomeKey : string.Empty);

        Dictionary<string, int> votes = new Dictionary<string, int>
        {
            { EchoOfHunger, 0 },
            { ComfortCollapse, 0 },
            { SyntheticGodhood, 0 },
        };

        AddVote(votes, hungerEnding);
        AddVote(votes, warEnding);
        AddVote(votes, medicalEnding);

        if (votes[EchoOfHunger] >= 2)
        {
            return EchoOfHunger;
        }

        if (votes[ComfortCollapse] >= 2)
        {
            return ComfortCollapse;
        }

        if (votes[SyntheticGodhood] >= 2)
        {
            return SyntheticGodhood;
        }

        if (!string.IsNullOrWhiteSpace(medicalEnding))
        {
            return medicalEnding;
        }

        if (!string.IsNullOrWhiteSpace(warEnding))
        {
            return warEnding;
        }

        if (!string.IsNullOrWhiteSpace(hungerEnding))
        {
            return hungerEnding;
        }

        return ComfortCollapse;
    }

    private static void AddVote(Dictionary<string, int> votes, string endingKey)
    {
        if (!string.IsNullOrWhiteSpace(endingKey) && votes.ContainsKey(endingKey))
        {
            votes[endingKey] += 1;
        }
    }

    private static string MapOutcomeToEnding(string sceneName, string outcomeKey)
    {
        switch (sceneName)
        {
            case "Test":
                switch (outcomeKey)
                {
                    case "Scarcity":
                        return EchoOfHunger;
                    case "Excess":
                        return ComfortCollapse;
                    case "Balance":
                        return SyntheticGodhood;
                }
                break;

            case "2":
                switch (outcomeKey)
                {
                    case "Resistance":
                        return EchoOfHunger;
                    case "Submission":
                        return ComfortCollapse;
                    case "Hesitation":
                        return SyntheticGodhood;
                }
                break;

            case "3":
                switch (outcomeKey)
                {
                    case "Despair":
                        return EchoOfHunger;
                    case "Supplication":
                        return ComfortCollapse;
                    case "Apotheosis":
                        return SyntheticGodhood;
                }
                break;
        }

        return string.Empty;
    }

    private static string BuildFinalVideoPrompt(
        string hungerTitle,
        string hungerSentence,
        string warTitle,
        string warSentence,
        string medicalTitle,
        string medicalSentence,
        string endingTitle,
        string endingSentence,
        List<string> tags)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("Create a cinematic first-person recap film of one human life passing through hunger, war, and medical transformation. ");
        builder.Append("The experience must feel like a single continuous subjective memory with coherent emotional escalation, bodily presence, and uninterrupted first-person camera language. ");
        builder.Append("Scene one outcome: ").Append(hungerTitle).Append(". ").Append(hungerSentence).Append(' ');
        builder.Append("Show a grounded survival perspective with visible hands or body implication, unstable breath, depleted surroundings, and the texture of deprivation pressing in from the edges of the frame. ");
        builder.Append("Scene two outcome: ").Append(warTitle).Append(". ").Append(warSentence).Append(' ');
        builder.Append("Shift into a militarized checkpoint memory with oppressive distance, threat silhouettes, pressure to obey, drifting ash, and a body reacting to command, fear, or refusal. ");
        builder.Append("Scene three outcome: ").Append(medicalTitle).Append(". ").Append(medicalSentence).Append(' ');
        builder.Append("Evolve into a ritual-medical chamber where scanners, pulse rings, prayer, contamination, or technological transcendence overtake the viewer's body in stages. ");
        builder.Append("Final ending identity: ").Append(endingTitle).Append(". ").Append(endingSentence).Append(' ');
        builder.Append("Resolve the journey as a final first-person revelation that visually fuses deprivation, surrender, faith, and transformation into one unmistakable ending state. ");

        if (tags != null && tags.Count > 0)
        {
            builder.Append("Use these recurring motifs across transitions: ").Append(string.Join(", ", tags)).Append(". ");
        }

        builder.Append("Favor dense atmosphere, practical human-scale environments, tactile surfaces, and emotionally legible motion. ");
        builder.Append("Transitions should feel motivated by memory and bodily sensation rather than hard cuts. ");
        builder.Append(NegativePromptClause);
        return builder.ToString();
    }

    private static string ResolveRecapSentence(string sceneName, SceneOutcomeRecord outcome, JourneySessionManager session)
    {
        return JourneyTextVariationComposer.BuildRecapSentence(sceneName, outcome, session);
    }

    private static List<EndingVideoSegmentSpec> BuildSegmentSpecs(
        JourneySessionManager session,
        string hungerTitle,
        string hungerSentence,
        string warTitle,
        string warSentence,
        string medicalTitle,
        string medicalSentence,
        string endingTitle,
        string endingSentence)
    {
        List<EndingVideoSegmentSpec> specs = new List<EndingVideoSegmentSpec>();
        const int segmentDurationSeconds = 4;

        if (session == null || !session.HasEndingVideoClip("hunger-memory"))
        {
            specs.Add(new EndingVideoSegmentSpec
            {
                segmentKey = "hunger-memory",
                displayTitle = "Hunger Memory",
                durationSeconds = segmentDurationSeconds,
                prompt = BuildSegmentPrompt(
                    segmentDurationSeconds,
                    "Create a first-person hunger memory.",
                    hungerTitle,
                    hungerSentence,
                    "Place the viewer inside a physically fragile environment with desperate gathering, unstable food fragments, survival instinct, and the feeling that every movement costs energy.",
                    "Use close bodily framing, harsh textures, shallow reserves, and camera continuity that feels like lived survival.")
            });
        }

        if (session == null || !session.HasEndingVideoClip("war-memory"))
        {
            specs.Add(new EndingVideoSegmentSpec
            {
                segmentKey = "war-memory",
                displayTitle = "War Memory",
                durationSeconds = segmentDurationSeconds,
                prompt = BuildSegmentPrompt(
                    segmentDurationSeconds,
                    "Create a first-person wartime surrender memory.",
                    warTitle,
                    warSentence,
                    "Show a ruined checkpoint, armed pressure, raised hands or defensive body posture, smoke, dust, fear, and collapsing certainty under command.",
                    "Keep the body scale believable, the threat external but immediate, and the motion tense, compressed, and psychologically trapped.")
            });
        }

        if (session == null || !session.HasEndingVideoClip("medical-memory"))
        {
            specs.Add(new EndingVideoSegmentSpec
            {
                segmentKey = "medical-memory",
                displayTitle = "Medical Memory",
                durationSeconds = segmentDurationSeconds,
                prompt = BuildSegmentPrompt(
                    segmentDurationSeconds,
                    "Create a first-person medical transformation memory.",
                    medicalTitle,
                    medicalSentence,
                    "Show ritual healing, plague residue, prayer, pulse rings, scanners, body-scale transformation, and a clear movement from vulnerability toward collapse, appeal, or transcendence.",
                    "Let the environment feel clinical and ceremonial at once, with strong atmospheric light, intimate perspective, and escalating bodily consequence.")
            });
        }

        specs.Add(new EndingVideoSegmentSpec
        {
            segmentKey = "final-identity",
            displayTitle = "Final Identity",
            durationSeconds = segmentDurationSeconds,
            prompt = BuildSegmentPrompt(
                segmentDurationSeconds,
                "Create a final first-person ending vision that resolves the whole journey.",
                endingTitle,
                endingSentence,
                "Blend hunger, war, and medical transformation into one decisive subjective revelation that makes the ending unmistakable.",
                "The final image should feel conclusive, cinematic, emotionally readable, and continuous with the earlier memories rather than detached from them.")
        });

        return specs;
    }

    private static string BuildSegmentPrompt(
        int durationSeconds,
        string opening,
        string title,
        string sentence,
        string visualDirection,
        string cinematicDirection)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(opening).Append(' ');
        builder.Append("Target duration: ").Append(durationSeconds).Append(" seconds. ");
        builder.Append("Outcome: ").Append(title).Append(". ");
        builder.Append(sentence).Append(' ');
        builder.Append(visualDirection).Append(' ');
        builder.Append(cinematicDirection).Append(' ');
        builder.Append("Maintain a grounded first-person camera with strong atmospheric continuity, tactile space, and emotionally clear progression. ");
        builder.Append(NegativePromptClause);
        return builder.ToString();
    }
}
