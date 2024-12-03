using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonApiImage
    {
        public enum EScale
        {
            fit = 0,
            fill, stretch
        }
        public class Options
        {
            [JsonConverter(typeof(StringEnumConverter))]
            [JsonProperty("scale")]
            public EScale Scale { get; set; }
            [JsonProperty("width")]
            public int Width { get; set; }
            [JsonProperty("height")]
            public int Height { get; set; }
            [JsonProperty("format")]
            public string Format { get; set; }
        }
        public class Image
        {
            [JsonProperty("image_key")]
            public string ImageKey { get; set; }
            [JsonProperty("options")]
            public Options Options { get; set; }
        }

        RoonApi _api;

        public RoonApiImage (RoonApi api)
        {
            _api = api; 
        }
        public async Task<byte[]> GetImage(Image image)
        {
            var result = await _api.SendReceive<byte[], Image>(RoonApi.ServiceImage + "/get_image", image);
            return result;
        }
    }
}
