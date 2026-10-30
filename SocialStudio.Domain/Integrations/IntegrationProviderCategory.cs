namespace SocialStudio.Domain.Integrations;

public enum IntegrationProviderCategory
{
    AiModel = 0,
    Design = 1,
    DataExtraction = 2,
    Publishing = 3,
    MediaGeneration = 4,

    // 5 was OfficialDataApi — RETIRED. It only ever held the YouTube Data API while Apify and Data365
    // sat under DataExtraction, so the taxonomy split equivalent providers across two groups. All data
    // sources now share DataExtraction ("Data providers"). Value 5 stays permanently unused: categories
    // persist by NAME and unknown names are filtered on read, so a legacy row simply drops the retired
    // category and the catalog seeder rewrites it on the next startup.
}