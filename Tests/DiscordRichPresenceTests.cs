using DiscordRPC;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;

namespace Spark
{
    [TestFixture]
    public class DiscordRichPresenceTests
    {
        private Mock<DiscordRpcClient> _mockDiscordRpcClient;

        [SetUp]
        public void Setup()
        {
            _mockDiscordRpcClient = new Mock<DiscordRpcClient>("test_client_id");
            DiscordRichPresence.discordClient = _mockDiscordRpcClient.Object;
        }

        [Test]
        public void TestProcessDiscordPresence_InLobby()
        {
            // Arrange
            Program.connectionState = Program.ConnectionState.InLobby;
            SparkSettings.instance.discordRichPresence = true;

            // Act
            DiscordRichPresence.ProcessDiscordPresence(null);

            // Assert
            _mockDiscordRpcClient.Verify(c => c.SetPresence(It.Is<RichPresence>(rp => rp.Details == "in EchoVR Lobby"))
            , Times.Once);
        }

        [Test]
        public void TestProcessDiscordPresence_InGame()
        {
            // Arrange
            Program.connectionState = Program.ConnectionState.InGame;
            SparkSettings.instance.discordRichPresence = true;
            var frame = new EchoVRAPI.Frame
            {
                map_name = "mpl_arena_a",
                private_match = false,
                teams = new List<EchoVRAPI.Team>
                {
                    new EchoVRAPI.Team { team = "BLUE", players = new List<EchoVRAPI.Player> { null } },
                    new EchoVRAPI.Team { team = "ORANGE", players = new List<EchoVRAPI.Player> { null } },
                    new EchoVRAPI.Team { team = "SPECTATOR", players = new List<EchoVRAPI.Player>() },
                },
                blue_points = 1,
                orange_points = 2,
                game_status = "playing",
                client_name = "test_player",
            };
            frame.teams[0].players[0] = new EchoVRAPI.Player { name = "test_player" };

            // Act
            DiscordRichPresence.ProcessDiscordPresence(frame);

            // Assert
            _mockDiscordRpcClient.Verify(c => c.SetPresence(It.Is<RichPresence>(rp =>
                rp.Details == "Arena Public (1 v 1): 1 - 2" &&
                rp.State == "Playing (blue)  - In Progress"
            )), Times.Once);
        }
    }
}
