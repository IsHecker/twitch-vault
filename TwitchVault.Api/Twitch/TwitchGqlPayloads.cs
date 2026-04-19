namespace TwitchVault.Api.Twitch;

internal static class TwitchGqlPayloads
{
    public static object[] StreamMetadata(string channel) =>
    [
        new
        {
            operationName = "UseLive",
            variables = new { channelLogin = channel },
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = "639d5f11bfb8bf3053b424d9ef650d04c4ebb7d94711d644afb08fe9a0fad5d9"
                }
            }
        },
        new
        {
            operationName = "VideoPreviewOverlay",
            variables = new { login = channel },
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = "9515480dee68a77e667cb19de634739d33f243572b007e98e67184b1a5d8369f"
                }
            }
        },
        new
        {
            operationName = "NielsenContentMetadata",
            variables = new
            {
                isCollectionContent = false,
                isLiveContent = true,
                isVODContent = false,
                collectionID = "",
                login = channel,
                vodID = ""
            },
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = "2dbf505ee929438369e68e72319d1106bb3c142e295332fac157c90638968586"
                }
            }
        }
    ];

    public static object PlaybackToken(string channel) => new
    {
        OperationName = "PlaybackAccessToken_Template",
        Query = """
                query PlaybackAccessToken_Template(
                    $login: String!,
                    $isLive: Boolean!,
                    $playerType: String!,
                    $platform: String!
                ) {
                    streamPlaybackAccessToken(
                        channelName: $login,
                        params: {
                            platform: $platform,
                            playerBackend: "mediaplayer",
                            playerType: $playerType
                        }
                    ) @include(if: $isLive) {
                        value
                        signature
                        authorization { isForbidden forbiddenReasonCode }
                        __typename
                    }
                }
                """,
        Variables = new
        {
            IsLive = true,
            Login = channel,
            PlayerType = "site",
            Platform = "web"
        }
    };

    public static object GetLatestVOD(string channel) => new
    {
        OperationName = "GetLatestVOD",
        Query = """
                query GetLatestVOD($login: String) {
                  user(login: $login) {
                    videos(first: 1, type: ARCHIVE) {
                      edges {
                        node { id }
                      }
                    }
                  }
                }
                """,
        Variables = new
        {
            login = channel
        }
    };

    public static object VideoMetadata(string vodId) => new
    {
        OperationName = "VideoMetadata",
        Query = """
                query VideoMetadata($videoID: ID) {
                  video(id: $videoID) {
                    previewThumbnailURL(width: 1920, height: 1080)
                  }
                }
                """,
        Variables = new
        {
            videoID = vodId
        }
    };
}