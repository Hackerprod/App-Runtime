using AndroidRuntime.Core.ApiLayer;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidApiRegistryTests
{
    [Fact]
    public void Registry_distinguishes_reference_overloads_with_the_same_shorty()
    {
        var registry = new AndroidApiRegistryBuilder()
            .Register("Lexample/Api;", "pick", "(Ljava/lang/Object;)I", (_, _) => 2)
            .Register("Lexample/Api;", "pick", "(Ljava/lang/String;)I", (_, _) => 1)
            .Build();

        Assert.True(registry.Contains(new("Lexample/Api;", "pick", "(Ljava/lang/String;)I")));
        Assert.True(registry.Contains(new("Lexample/Api;", "pick", "(Ljava/lang/Object;)I")));
    }

    [Fact]
    public void Activity_lifecycle_registry_exposes_only_the_supported_exact_base_stubs()
    {
        var registry = AndroidApiRegistry.CreateActivityLifecycleRegistry();

        Assert.True(registry.Contains(new("Landroid/app/Activity;", "<init>", "()V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onCreate", "(Landroid/os/Bundle;)V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onStart", "()V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onResume", "()V")));
        Assert.False(registry.Contains(new("Landroid/app/Activity;", "onCreate", "()V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onPause", "()V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onStop", "()V")));
        Assert.True(registry.Contains(new("Landroid/app/Activity;", "onDestroy", "()V")));
    }
}
