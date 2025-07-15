using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Spark.Tests;

namespace Spark
{
    [TestFixture]
    public class DiscordOAuthTests
    {
        private MockDiscordAPI _mockDiscordAPI;

        [SetUp]
        public void Setup()
        {
            _mockDiscordAPI = new MockDiscordAPI("http://localhost:6722/");
            _mockDiscordAPI.Start();
        }

        [TearDown]
        public void Teardown()
        {
            _mockDiscordAPI.Stop();
        }

        [Test]
        public void TestOAuthLogin_Success()
        {
            // Arrange
            var expectedToken = "test_token";
            var expectedRefreshToken = "test_refresh_token";
            var expectedUsername = "test_user";
            var expectedUserId = "12345";

            _mockDiscordAPI.AddResponse("/oauth2/token", new Dictionary<string, string>
            {
                { "access_token", expectedToken },
                { "refresh_token", expectedRefreshToken }
            });

            _mockDiscordAPI.AddResponse("/users/@me", new Dictionary<string, string>
            {
                { "username", expectedUsername },
                { "id", expectedUserId },
                { "avatar", "test_avatar" }
            });

            _mockDiscordAPI.AddResponse($"/auth/token/{expectedToken}?u={SparkSettings.instance.client_name}&v={Program.AppVersionString()}", new Dictionary<string, object>
            {
                { "keys", new List<DiscordOAuth.AccessCodeKey>() },
                { "write", "test_write_key" },
                { "firebase_cred", "test_firebase_cred" }
            });

            // Act
            DiscordOAuth.OAuthLogin(true);

            // Assert
            Thread.Sleep(100); // give it a moment to process
            Assert.Equals(expectedToken, DiscordOAuth.oauthToken);
            Assert.Equals(expectedUsername, DiscordOAuth.DiscordUsername);
            Assert.Equals(expectedUserId, DiscordOAuth.DiscordUserID);
        }

        [Test]
        public void TestOAuthLogin_ApiError()
        {
            // Arrange
            _mockDiscordAPI.AddResponse("/oauth2/token", new Dictionary<string, string>
            {
                { "error", "invalid_grant" }
            });

            // Act
            DiscordOAuth.OAuthLogin(true);

            // Assert
            Thread.Sleep(100); // give it a moment to process
            Assert.Equals(DiscordOAuth.oauthToken, "");
        }
    }
}
