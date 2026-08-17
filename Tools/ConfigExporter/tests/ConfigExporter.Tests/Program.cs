namespace ConfigExporter.Tests;

public static class Program
{
    public static int Main()
    {
        return TestRunner.Run(
            ("SchemaLoader.valid", SchemaLoaderTests.ValidSchemaReadsColumnsAndRules),
            ("SchemaLoader.unknownType", SchemaLoaderTests.UnknownColumnTypeThrows),
            ("SchemaLoader.keyNotColumn", SchemaLoaderTests.KeyNotAColumnThrows),

            ("Validation.validRows", ValidationEngineTests.ValidRowsNoErrors),
            ("Validation.headerMismatch", ValidationEngineTests.HeaderMismatchError),
            ("Validation.duplicateKey", ValidationEngineTests.DuplicateKeyError),
            ("Validation.badEnum", ValidationEngineTests.BadEnumError),
            ("Validation.zeroHp", ValidationEngineTests.ZeroHpError),
            ("Validation.nonSettlerZeroInterval", ValidationEngineTests.NonSettlerZeroAttackIntervalError),
            ("Validation.settlerZeroIntervalAllowed", ValidationEngineTests.SettlerZeroAttackIntervalAllowed),

            ("Output.csvDeterministic", OutputWriterTests.CsvIsDeterministic),
            ("Output.jsonFieldOrderDeterministic", OutputWriterTests.JsonFieldOrderDeterministic),
            ("Output.manifestWritesHashes", OutputWriterTests.ManifestWritesHashes),

            ("RoundTrip.initReadValidate", WorkbookRoundTripTests.InitThenReadThenValidate));
    }
}
