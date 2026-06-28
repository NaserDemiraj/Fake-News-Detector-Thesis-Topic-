using FakeNewsDetector.Evaluation;

// compare mode: reads multiple metrics JSON files and prints side-by-side table
if (args.Length > 0 && args[0] == "compare")
{
    await CompareMode.RunAsync(args[1..]);
    return;
}

await EvaluationRunner.RunAsync(args);
