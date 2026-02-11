using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Helpers;

namespace TelegramGroupsAdmin.UnitTests.Helpers;

/// <summary>
/// Unit tests for ServiceMessageHelper.IsServiceMessage
/// Tests the service message classification logic with various message types and config combinations
/// </summary>
[TestFixture]
public class ServiceMessageHelperTests
{
    #region Regular Messages (Not Service Messages)

    [Test]
    public void IsServiceMessage_TextMessage_ReturnsFalse()
    {
        // Arrange
        var message = new Message { Text = "Hello world", Chat = new Chat { Id = 123 } };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.False);
            Assert.That(shouldDelete, Is.False);
        }
    }

    [Test]
    public void IsServiceMessage_PhotoMessage_ReturnsFalse()
    {
        // Arrange
        var message = new Message
        {
            Photo = [new PhotoSize { FileId = "abc", FileUniqueId = "xyz", Width = 100, Height = 100 }],
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.False);
            Assert.That(shouldDelete, Is.False);
        }
    }

    [Test]
    public void IsServiceMessage_EmptyMessage_ReturnsFalse()
    {
        // Arrange
        var message = new Message { Chat = new Chat { Id = 123 } };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.False);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Join Messages (NewChatMembers)

    [Test]
    public void IsServiceMessage_JoinMessage_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            NewChatMembers = [new User { Id = 456, FirstName = "Test" }],
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True, "Default config should delete join messages");
        }
    }

    [Test]
    public void IsServiceMessage_JoinMessage_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            NewChatMembers = [new User { Id = 456, FirstName = "Test" }],
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeleteJoinMessages = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True, "Still a service message");
            Assert.That(shouldDelete, Is.False, "Config says don't delete");
        }
    }

    #endregion

    #region Leave Messages (LeftChatMember)

    [Test]
    public void IsServiceMessage_LeaveMessage_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            LeftChatMember = new User { Id = 456, FirstName = "Test" },
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True, "Default config should delete leave messages");
        }
    }

    [Test]
    public void IsServiceMessage_LeaveMessage_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            LeftChatMember = new User { Id = 456, FirstName = "Test" },
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeleteLeaveMessages = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Photo Changes (NewChatPhoto / DeleteChatPhoto)

    [Test]
    public void IsServiceMessage_NewChatPhoto_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            NewChatPhoto = [new PhotoSize { FileId = "abc", FileUniqueId = "xyz", Width = 100, Height = 100 }],
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_DeleteChatPhoto_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            DeleteChatPhoto = true,
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_PhotoChange_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            NewChatPhoto = [new PhotoSize { FileId = "abc", FileUniqueId = "xyz", Width = 100, Height = 100 }],
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeletePhotoChanges = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Title Changes (NewChatTitle)

    [Test]
    public void IsServiceMessage_TitleChange_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            NewChatTitle = "New Group Name",
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_TitleChange_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            NewChatTitle = "New Group Name",
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeleteTitleChanges = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Pinned Messages

    [Test]
    public void IsServiceMessage_PinnedMessage_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            PinnedMessage = new Message { Text = "Pinned content", Chat = new Chat { Id = 123 } },
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_PinnedMessage_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            PinnedMessage = new Message { Text = "Pinned content", Chat = new Chat { Id = 123 } },
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeletePinNotifications = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Chat Creation Messages

    [Test]
    public void IsServiceMessage_GroupChatCreated_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            GroupChatCreated = true,
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_SupergroupChatCreated_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            SupergroupChatCreated = true,
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_ChannelChatCreated_ReturnsTrue()
    {
        // Arrange
        var message = new Message
        {
            ChannelChatCreated = true,
            Chat = new Chat { Id = 123 }
        };
        var config = ServiceMessageDeletionConfig.Default;

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.True);
        }
    }

    [Test]
    public void IsServiceMessage_ChatCreation_WhenDisabled_ShouldDeleteFalse()
    {
        // Arrange
        var message = new Message
        {
            GroupChatCreated = true,
            Chat = new Chat { Id = 123 }
        };
        var config = new ServiceMessageDeletionConfig { DeleteChatCreationMessages = false };

        // Act
        var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(isServiceMessage, Is.True);
            Assert.That(shouldDelete, Is.False);
        }
    }

    #endregion

    #region Config Combinations

    [Test]
    public void IsServiceMessage_AllConfigDisabled_AllShouldDeleteFalse()
    {
        // Arrange - all toggles off
        var config = new ServiceMessageDeletionConfig
        {
            DeleteJoinMessages = false,
            DeleteLeaveMessages = false,
            DeletePhotoChanges = false,
            DeleteTitleChanges = false,
            DeletePinNotifications = false,
            DeleteChatCreationMessages = false
        };

        var messages = new Message[]
        {
            new() { NewChatMembers = [new User { Id = 1, FirstName = "A" }], Chat = new Chat { Id = 123 } },
            new() { LeftChatMember = new User { Id = 1, FirstName = "A" }, Chat = new Chat { Id = 123 } },
            new() { NewChatPhoto = [new PhotoSize { FileId = "a", FileUniqueId = "b", Width = 1, Height = 1 }], Chat = new Chat { Id = 123 } },
            new() { NewChatTitle = "Title", Chat = new Chat { Id = 123 } },
            new() { PinnedMessage = new Message { Text = "Pin", Chat = new Chat { Id = 123 } }, Chat = new Chat { Id = 123 } },
            new() { GroupChatCreated = true, Chat = new Chat { Id = 123 } }
        };

        // Act & Assert - all are service messages but none should be deleted
        foreach (var message in messages)
        {
            var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(isServiceMessage, Is.True, $"Message should be recognized as service message");
                Assert.That(shouldDelete, Is.False, $"shouldDelete should be false when config disabled");
            }
        }
    }

    [Test]
    public void IsServiceMessage_DefaultConfig_AllShouldDeleteTrue()
    {
        // Arrange - default config (all enabled)
        var config = ServiceMessageDeletionConfig.Default;

        var messages = new Message[]
        {
            new() { NewChatMembers = [new User { Id = 1, FirstName = "A" }], Chat = new Chat { Id = 123 } },
            new() { LeftChatMember = new User { Id = 1, FirstName = "A" }, Chat = new Chat { Id = 123 } },
            new() { NewChatPhoto = [new PhotoSize { FileId = "a", FileUniqueId = "b", Width = 1, Height = 1 }], Chat = new Chat { Id = 123 } },
            new() { NewChatTitle = "Title", Chat = new Chat { Id = 123 } },
            new() { PinnedMessage = new Message { Text = "Pin", Chat = new Chat { Id = 123 } }, Chat = new Chat { Id = 123 } },
            new() { GroupChatCreated = true, Chat = new Chat { Id = 123 } }
        };

        // Act & Assert - all should be deleted with default config
        foreach (var message in messages)
        {
            var isServiceMessage = ServiceMessageHelper.IsServiceMessage(message, config, out var shouldDelete);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(isServiceMessage, Is.True);
                Assert.That(shouldDelete, Is.True, "Default config should delete all service messages");
            }
        }
    }

    #endregion

    #region GetServiceMessageText Tests

    [Test]
    public void GetServiceMessageText_JoinMessage_SelfJoin_ReturnsJoinedText()
    {
        var user = new User { Id = 456, FirstName = "John", LastName = "Doe" };
        var message = new Message
        {
            From = user,
            NewChatMembers = [user],
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("John Doe joined the group"));
    }

    [Test]
    public void GetServiceMessageText_JoinMessage_AddedByAdmin_ReturnsAddedText()
    {
        var admin = new User { Id = 999, FirstName = "Admin" };
        var user = new User { Id = 456, FirstName = "John" };
        var message = new Message
        {
            From = admin,
            NewChatMembers = [user],
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Admin added John"));
    }

    [Test]
    public void GetServiceMessageText_JoinMessage_MultipleUsers_ReturnsAddedAllText()
    {
        var admin = new User { Id = 999, FirstName = "Admin" };
        var user1 = new User { Id = 456, FirstName = "John" };
        var user2 = new User { Id = 789, FirstName = "Jane" };
        var message = new Message
        {
            From = admin,
            NewChatMembers = [user1, user2],
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Admin added John, Jane"));
    }

    [Test]
    public void GetServiceMessageText_LeaveMessage_ReturnsLeftText()
    {
        var user = new User { Id = 456, FirstName = "John", LastName = "Doe" };
        var message = new Message
        {
            LeftChatMember = user,
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("John Doe left the group"));
    }

    [Test]
    public void GetServiceMessageText_TitleChange_ReturnsTitleText()
    {
        var message = new Message
        {
            NewChatTitle = "New Group Name",
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Group name changed to \"New Group Name\""));
    }

    [Test]
    public void GetServiceMessageText_PhotoChange_ReturnsPhotoUpdatedText()
    {
        var message = new Message
        {
            NewChatPhoto = [new PhotoSize { FileId = "abc", FileUniqueId = "xyz", Width = 100, Height = 100 }],
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Group photo updated"));
    }

    [Test]
    public void GetServiceMessageText_PhotoDelete_ReturnsPhotoRemovedText()
    {
        var message = new Message
        {
            DeleteChatPhoto = true,
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Group photo removed"));
    }

    [Test]
    public void GetServiceMessageText_PinnedMessage_ReturnsPinnedText()
    {
        var message = new Message
        {
            PinnedMessage = new Message { Text = "Important", Chat = new Chat { Id = 123 } },
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.EqualTo("Message pinned"));
    }

    [Test]
    public void GetServiceMessageText_RegularMessage_ReturnsNull()
    {
        var message = new Message
        {
            Text = "Hello world",
            Chat = new Chat { Id = 123 }
        };

        var result = ServiceMessageHelper.GetServiceMessageText(message);
        Assert.That(result, Is.Null);
    }

    #endregion
}
