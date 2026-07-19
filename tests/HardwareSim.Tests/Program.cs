var tests = new List<TestCase>();
tests.AddRange(ExactCycleGoldenTests.All);
tests.AddRange(PaperExperimentTests.All);
tests.AddRange(CimEgressAccountingTests.All);
tests.AddRange(OpticalInterventionTests.All);
tests.AddRange(NocAndHoldoutTests.All);
tests.AddRange(Phase10BooksimMatchedNocTests.All);
return TestRunner.Run(tests, args);
