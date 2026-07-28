namespace TwitchVault.Api.Twitch;

internal static class TwitchGqlPayloads
{
    public static object GetLiveStatus(string channel) => new
    {
        OperationName = "GetLiveStatus",
        Query = """
                query GetLiveStatus($login: String!) {
                    user(login: $login) {
                        stream {
                            id
                            createdAt
                        }
                    }
                }
                """,
        Variables = new
        {
            login = channel
        }
    };

    public static object StreamMetadata(string channel) => new
    {
        OperationName = "GetBroadcastSettings",
        Query = """
                query GetBroadcastSettings($login: String!) {
                    user(login: $login) {
                        stream {
                            id
                            createdAt
                        }
                        broadcastSettings {
                            title
                            game {
                                id
                                name
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
            Platform = "pwa"
        }
    };

    public static object GetStreamVOD(string channel) => new
    {
        OperationName = "GetStreamVOD",
        Query = """
                query GetStreamVOD($login: String) {
                  user(login: $login) {
                    stream {
                      id
                      archiveVideo {
                        id
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

    public static object GetChannelId(string channel) => new
    {
        OperationName = "GetUserId",
        Query = """
                query GetUserId($login: String!) {
                    user(login: $login) {
                        id
                    }
                }
                """,
        Variables = new
        {
            login = channel
        }
    };
}