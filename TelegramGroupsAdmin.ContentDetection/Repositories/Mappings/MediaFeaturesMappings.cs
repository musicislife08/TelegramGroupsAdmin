using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>Maps the Core media-features model to and from the Data JSON contract.</summary>
public static class MediaFeaturesMappings
{
    extension(MediaFeatures features)
    {
        public DataModels.MediaFeaturesDto ToDto() => features switch
        {
            PhotoFeatures p => new DataModels.PhotoFeaturesDto(p.Hash),
            VideoFeatures v => new DataModels.VideoFeaturesDto([.. v.Keyframes.Select(k => new DataModels.KeyframeFeatureDto(k.Position, k.Hash))]),
            _ => throw new ArgumentOutOfRangeException(nameof(features), features.GetType().Name, "Unknown media features type")
        };
    }

    extension(DataModels.MediaFeaturesDto dto)
    {
        public MediaFeatures ToModel() => dto switch
        {
            DataModels.PhotoFeaturesDto p => new PhotoFeatures(p.Hash),
            DataModels.VideoFeaturesDto v => new VideoFeatures([.. v.Keyframes.Select(k => new KeyframeFeature(k.Position, k.Hash))]),
            _ => throw new ArgumentOutOfRangeException(nameof(dto), dto.GetType().Name, "Unknown media features type")
        };
    }
}
