using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public void SaveLoadRoundTripPreservesNameAndSettings()
    {
        ProfileService service = new();
        string name = $"test-{Guid.NewGuid():N}";
        try
        {
            AppProfile saved = new()
            {
                Name = name,
                Description = "profil de test",
                Settings = new AppSettings { Language = "en", FontSize = 13 }
            };

            service.Save(saved);

            AppProfile? loaded = service.Load(name);

            Assert.NotNull(loaded);
            Assert.Equal(name, loaded!.Name);
            Assert.Equal("profil de test", loaded.Description);
            Assert.Equal("en", loaded.Settings.Language);
            Assert.Equal(13.0, loaded.Settings.FontSize);
        }
        finally
        {
            service.Delete(name);
        }
    }

    [Fact]
    public void GetAllProfilesContainsSavedProfile()
    {
        ProfileService service = new();
        string name = $"test-{Guid.NewGuid():N}";
        try
        {
            service.Save(new AppProfile { Name = name });

            List<string> names = service.GetAllProfiles().Select(p => p.Name).ToList();

            Assert.Contains(name, names);
        }
        finally
        {
            service.Delete(name);
        }
    }

    [Fact]
    public void RenameChangesNameAndRemovesOldFile()
    {
        ProfileService service = new();
        string oldName = $"test-{Guid.NewGuid():N}";
        string newName = $"test-{Guid.NewGuid():N}";
        try
        {
            service.Save(new AppProfile { Name = oldName, Settings = new AppSettings { Language = "fr" } });

            service.Rename(oldName, newName);

            Assert.Null(service.Load(oldName));

            AppProfile? renamed = service.Load(newName);
            Assert.NotNull(renamed);
            Assert.Equal(newName, renamed!.Name);
            Assert.Equal("fr", renamed.Settings.Language);
        }
        finally
        {
            service.Delete(oldName);
            service.Delete(newName);
        }
    }

    [Fact]
    public void DeleteRemovesProfile()
    {
        ProfileService service = new();
        string name = $"test-{Guid.NewGuid():N}";

        service.Save(new AppProfile { Name = name });
        Assert.NotNull(service.Load(name));

        service.Delete(name);

        Assert.Null(service.Load(name));
    }

    [Fact]
    public void LoadNonexistentProfileReturnsNull()
    {
        ProfileService service = new();

        Assert.Null(service.Load($"inexistant-{Guid.NewGuid():N}"));
    }

    [Fact]
    public void RenameNonexistentProfileDoesNothing()
    {
        ProfileService service = new();
        string oldName = $"ghost-{Guid.NewGuid():N}";
        string newName = $"ghost-{Guid.NewGuid():N}";
        try
        {
            service.Rename(oldName, newName);

            Assert.Null(service.Load(oldName));
            Assert.Null(service.Load(newName));
        }
        finally
        {
            service.Delete(oldName);
            service.Delete(newName);
        }
    }

    [Theory]
    [InlineData("martin")]
    [InlineData("Jean")]
    public void AvatarInitialsReturnsUppercaseFirstLetter(string name)
    {
        AppProfile profile = new() { Name = name };

        Assert.Equal(name[..1].ToUpper(System.Globalization.CultureInfo.InvariantCulture), profile.AvatarInitials);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AvatarInitialsEmptyNameReturnsQuestionMark(string? name)
    {
        AppProfile profile = new() { Name = name ?? string.Empty };

        Assert.Equal("?", profile.AvatarInitials);
    }
}
