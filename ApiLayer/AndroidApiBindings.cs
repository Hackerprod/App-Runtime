#nullable disable
// NOTE: this file is a decompiled restoration of the original (see incident). Its
// nullability annotations were lost in decompilation; nullable analysis is disabled
// until the file is properly rewritten from the compiled behavior. Functionality is
// validated by the full test suite.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.ApiLayer;

public static class AndroidApiBindings
{
	public static AndroidApiMethodId SetTitle { get; } = Api("Landroid/app/Activity;", "setTitle", "(Ljava/lang/CharSequence;)V");

	public static AndroidApiMethodId LogInfo { get; } = Api("Landroid/util/Log;", "i", "(Ljava/lang/String;Ljava/lang/String;)I");

	public static AndroidApiRegistryBuilder CreateBuilder(ActivityWindowPeers peers, IAndroidLogSink logSink)
	{
		return CreateBuilder(new AndroidFrameworkState("standalone", string.Empty, string.Empty, peers), logSink);
	}

	public static AndroidApiRegistryBuilder CreateBuilder(AndroidFrameworkState state, IAndroidLogSink logSink)
	{
		ArgumentNullException.ThrowIfNull(state, "state");
		ArgumentNullException.ThrowIfNull(logSink, "logSink");
		AndroidApiRegistryBuilder builder = new AndroidApiRegistryBuilder();
		JavaLangBindings.Register(builder, state);
		JavaLangThreadBindings.Register(builder, state);
		JavaUtilConcurrentExecutorBindings.Register(builder, state);
		AndroidOsHandlerBindings.Register(builder, state);
		RegisterVoid(builder, "Landroid/app/Activity;", "<init>", "()V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onCreate", "(Landroid/os/Bundle;)V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onStart", "()V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onResume", "()V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onPause", "()V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onStop", "()V");
		RegisterVoid(builder, "Landroid/app/Activity;", "onDestroy", "()V");
		RegisterVoid(builder, "Landroid/os/BaseBundle;", "<init>", "()V");
		builder.Register(SetTitle, (AndroidApiInvocation invocation, object[] args) => SetActivityTitle(state, invocation, args));
		builder.Register(Api("Landroid/app/Activity;", "getTitle", "()Ljava/lang/CharSequence;"), (AndroidApiInvocation _, object[] args) => RequireWindow(state, RequireActivity(state, Receiver(args))).Title);
		builder.Register(Api("Landroid/app/Activity;", "getLocalClassName", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => LocalClassName(state, RequireActivity(state, Receiver(args))));
		builder.Register(Api("Landroid/app/Activity;", "getIntent", "()Landroid/content/Intent;"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireActivity(state, Receiver(args));
			return state.LauncherIntent;
		});
		builder.Register(Api("Landroid/app/Activity;", "finish", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireActivity(state, Receiver(args));
			state.RequestFinish();
			return null!;
		});
		builder.Register(Api("Landroid/app/Activity;", "isFinishing", "()Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireActivity(state, Receiver(args));
			return state.IsFinishing ? 1 : 0;
		});
		builder.Register(Api("Landroid/app/Activity;", "isDestroyed", "()Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireActivity(state, Receiver(args));
			return state.IsDestroyed ? 1 : 0;
		});
		RegisterViews(builder, state);
		builder.Register(Api("Landroid/content/Context;", "getPackageName", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireContext(state, Receiver(args));
			return state.PackageName;
		});
		builder.Register(Api("Landroid/content/Context;", "getApplicationContext", "()Landroid/content/Context;"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireContext(state, Receiver(args));
			return state.ApplicationContext;
		});
		RegisterLogs(builder, state, logSink);
		RegisterText(builder, state);
		RegisterStrings(builder, state);
		RegisterStringBuilder(builder, state);
		RegisterColor(builder);
		RegisterSystemClock(builder, state);
		RegisterThrowables(builder);
		AndroidSystemServiceBindings.Register(builder, state);
		RegisterBundles(builder, state);
		RegisterIntents(builder, state);
		RegisterToasts(builder, state);
		JavaUtilConcurrentAtomicBindings.Register(builder, state);
		RegisterWeakHashMaps(builder, state);
		RegisterHashMaps(builder, state);
		RegisterArrayLists(builder, state);
		RegisterCopyOnWriteArrayLists(builder, state);
		RegisterIterators(builder, state);
		RegisterWeakReferences(builder, state);
		RegisterCopyOnWriteArraySets(builder, state);
		return builder;
	}

	private static void RegisterWeakHashMaps(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/WeakHashMap;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.WeakHashMaps.Add(Receiver(args), new WeakHashMapPeer());
			return null!;
		});
		builder.Register(Api("Ljava/util/WeakHashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			WeakHashMapPeer weakHashMapPeer = state.WeakHashMaps.Get(Receiver(args));
			object result = (weakHashMapPeer.Entries.TryGetValue(args[1], out object value) ? value : null);
			weakHashMapPeer.Entries[args[1]] = args[2];
			return result;
		});
		builder.Register(Api("Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.WeakHashMaps.Get(Receiver(args)).Entries.TryGetValue(args[1], out object value) ? value : null);
		builder.Register(Api("Ljava/util/WeakHashMap;", "containsKey", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.WeakHashMaps.Get(Receiver(args)).Entries.ContainsKey(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/WeakHashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			WeakHashMapPeer weakHashMapPeer = state.WeakHashMaps.Get(Receiver(args));
			object result = (weakHashMapPeer.Entries.TryGetValue(args[1], out object value) ? value : null);
			weakHashMapPeer.Entries.Remove(args[1]);
			return result;
		});
		builder.Register(Api("Ljava/util/WeakHashMap;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.WeakHashMaps.Get(Receiver(args)).Entries.Count);
	}

	private static void RegisterHashMaps(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/HashMap;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.HashMaps.Add(Receiver(args), new HashMapPeer());
			return null!;
		});
		builder.Register(Api("Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Put(args[1], args[2]));
		builder.Register(Api("Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Get(args[1]));
		builder.Register(Api("Ljava/util/HashMap;", "containsKey", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).ContainsKey(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/HashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Remove(args[1]));
		builder.Register(Api("Ljava/util/HashMap;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Count);
	}

	private static void RegisterArrayLists(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/ArrayList;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.ArrayLists.Add(Receiver(args), new ListPeer());
			return null!;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "<init>", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireInt(args[1]);
			state.ArrayLists.Add(Receiver(args), new ListPeer());
			return null!;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.ArrayLists.Get(Receiver(args)).Elements.Add(args[1]);
			return 1;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "add", "(ILjava/lang/Object;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			int num = RequireInt(args[1]);
			if ((uint)num > (uint)listPeer.Elements.Count)
			{
				throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IndexOutOfBoundsException;"));
			}
			listPeer.Elements.Insert(num, args[2]);
			return null!;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			return listPeer.Elements[index];
		});
		builder.Register(Api("Ljava/util/ArrayList;", "set", "(ILjava/lang/Object;)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			object result = listPeer.Elements[index];
			listPeer.Elements[index] = args[2];
			return result;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "contains", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.ArrayLists.Get(Receiver(args)).Elements.Contains(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/ArrayList;", "indexOf", "(Ljava/lang/Object;)I"), (AndroidApiInvocation _, object[] args) => state.ArrayLists.Get(Receiver(args)).Elements.IndexOf(args[1]));
		builder.Register(Api("Ljava/util/ArrayList;", "clear", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.ArrayLists.Get(Receiver(args)).Elements.Clear();
			return null!;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.ArrayLists.Get(Receiver(args)).Elements.Count);
		builder.Register(Api("Ljava/util/ArrayList;", "isEmpty", "()Z"), (AndroidApiInvocation _, object[] args) => (state.ArrayLists.Get(Receiver(args)).Elements.Count == 0) ? 1 : 0);
		builder.Register(Api("Ljava/util/ArrayList;", "remove", "(I)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			object result = listPeer.Elements[index];
			listPeer.Elements.RemoveAt(index);
			return result;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "iterator", "()Ljava/util/Iterator;"), (AndroidApiInvocation _, object[] args) => CreateIterator(state, state.ArrayLists.Get(Receiver(args)).Elements));
	}

	private static void RegisterCopyOnWriteArrayLists(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.CopyOnWriteArrayLists.Add(Receiver(args), new ListPeer());
			return null!;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "(Ljava/util/Collection;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = new ListPeer();
			if (!TryCopyGuestCollection(state, args[1], listPeer.Elements))
			{
				throw new InvalidOperationException("CopyOnWriteArrayList Collection constructor source is not a modeled guest collection.");
			}
			state.CopyOnWriteArrayLists.Add(Receiver(args), listPeer);
			return null!;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "add", "(Ljava/lang/Object;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Add(args[1]);
			return 1;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "add", "(ILjava/lang/Object;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.CopyOnWriteArrayLists.Get(Receiver(args));
			int num = RequireInt(args[1]);
			if ((uint)num > (uint)listPeer.Elements.Count)
			{
				throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IndexOutOfBoundsException;"));
			}
			listPeer.Elements.Insert(num, args[2]);
			return null!;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "get", "(I)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.CopyOnWriteArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			return listPeer.Elements[index];
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "set", "(ILjava/lang/Object;)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.CopyOnWriteArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			object result = listPeer.Elements[index];
			listPeer.Elements[index] = args[2];
			return result;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "remove", "(I)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.CopyOnWriteArrayLists.Get(Receiver(args));
			int index = RequireInt(args[1]);
			RequireListIndex(listPeer.Elements, index);
			object result = listPeer.Elements[index];
			listPeer.Elements.RemoveAt(index);
			return result;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "remove", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Remove(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "contains", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Contains(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "indexOf", "(Ljava/lang/Object;)I"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.IndexOf(args[1]));
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Count);
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "isEmpty", "()Z"), (AndroidApiInvocation _, object[] args) => (state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Count == 0) ? 1 : 0);
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "clear", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements.Clear();
			return null!;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "addIfAbsent", "(Ljava/lang/Object;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.CopyOnWriteArrayLists.Get(Receiver(args));
			if (listPeer.Elements.Contains(args[1]))
			{
				return 0;
			}
			listPeer.Elements.Add(args[1]);
			return 1;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArrayList;", "iterator", "()Ljava/util/Iterator;"), (AndroidApiInvocation _, object[] args) => CreateIterator(state, state.CopyOnWriteArrayLists.Get(Receiver(args)).Elements));
	}

	private static void RegisterIterators(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/Iterator;", "hasNext", "()Z"), (AndroidApiInvocation _, object[] args) => state.Iterators.Get(Receiver(args)).HasNext ? 1 : 0);
		builder.Register(Api("Ljava/util/Iterator;", "next", "()Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.Iterators.Get(Receiver(args)).Next());
	}

	private static DexObject CreateIterator(AndroidFrameworkState state, IEnumerable<object> snapshot)
	{
		DexObject iterator = new DexObject("Ljava/util/Iterator;");
		state.Iterators.Add(iterator, new IteratorPeer(snapshot));
		return iterator;
	}

	private static void RequireListIndex(List<object> elements, int index)
	{
		if ((uint)index >= (uint)elements.Count)
		{
			throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IndexOutOfBoundsException;"));
		}
	}

	private static bool TryCopyGuestCollection(AndroidFrameworkState state, object source, List<object> target)
	{
		if (!(source is DexObject guest))
		{
			return false;
		}
		AndroidPeerStore<ListPeer>[] array = new AndroidPeerStore<ListPeer>[2] { state.ArrayLists, state.CopyOnWriteArrayLists };
		foreach (AndroidPeerStore<ListPeer> store in array)
		{
			try
			{
				target.AddRange(store.Get(guest).Elements);
				return true;
			}
			catch (KeyNotFoundException)
			{
			}
		}
		try
		{
			target.AddRange(state.CopyOnWriteArraySets.Get(guest));
			return true;
		}
		catch (KeyNotFoundException)
		{
		}
		return false;
	}

	private static void RegisterWeakReferences(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/lang/ref/WeakReference;", "<init>", "(Ljava/lang/Object;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.WeakReferences.Add(Receiver(args), new WeakReferencePeer
			{
				Value = args[1]
			});
			return null!;
		});
		builder.Register(Api("Ljava/lang/ref/WeakReference;", "get", "()Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.WeakReferences.Get(Receiver(args)).Value);
		builder.Register(Api("Ljava/lang/ref/WeakReference;", "clear", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.WeakReferences.Get(Receiver(args)).Value = null;
			return null!;
		});
	}

	private static void RegisterCopyOnWriteArraySets(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArraySet;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.CopyOnWriteArraySets.Add(Receiver(args), new HashSet<object>());
			return null!;
		});
		builder.Register(Api("Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArraySets.Get(Receiver(args)).Add(args[1]) ? 1 : 0);
	}

	private static void RegisterViews(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Landroid/app/Activity;", "setContentView", "(I)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			RequireUi(state).SetContentView(RequireInt(args[1]));
			return null!;
		});
		builder.Register(Api("Landroid/app/Activity;", "findViewById", "(I)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return RequireUi(state).FindViewById(RequireInt(args[1]));
		});
		builder.Register(Api("Landroid/view/View;", "findViewById", "(I)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).FindViewById(RequireInt(args[1]), Receiver(args));
		});
		builder.Register(Api("Landroid/view/View;", "getId", "()I"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).GetId(Receiver(args));
		});
		builder.Register(Api("Landroid/view/View;", "setEnabled", "(Z)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireUi(state).SetEnabled(Receiver(args), RequireInt(args[1]) != 0);
			return null!;
		});
		builder.Register(Api("Landroid/view/View;", "isEnabled", "()Z"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).IsEnabled(Receiver(args)) ? 1 : 0;
		});
		builder.Register(Api("Landroid/view/View;", "setVisibility", "(I)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireUi(state).SetVisibility(Receiver(args), RequireInt(args[1]));
			return null!;
		});
		builder.Register(Api("Landroid/view/View;", "getVisibility", "()I"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).GetVisibility(Receiver(args));
		});
		builder.Register(Api("Landroid/view/View;", "setOnClickListener", "(Landroid/view/View$OnClickListener;)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireUi(state).SetOnClickListener(Receiver(args), OptionalDex(args[1]));
			return null!;
		});
		builder.Register(Api("Landroid/view/View;", "performClick", "()Z"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).PerformClick(Receiver(args)) ? 1 : 0;
		});
		builder.Register(Api("Landroid/widget/TextView;", "setText", "(Ljava/lang/CharSequence;)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireUi(state).SetText(Receiver(args), AsText(state, args[1]));
			return null!;
		});
		builder.Register(Api("Landroid/widget/TextView;", "getText", "()Ljava/lang/CharSequence;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).GetText(Receiver(args));
		});
	}

	private static void RegisterThrowables(AndroidApiRegistryBuilder builder)
	{
		string[] types = new string[16]
		{
			"Ljava/lang/Throwable;", "Ljava/lang/Error;", "Ljava/lang/LinkageError;", "Ljava/lang/IncompatibleClassChangeError;", "Ljava/lang/NoSuchMethodError;", "Ljava/lang/Exception;", "Ljava/lang/RuntimeException;", "Ljava/lang/NullPointerException;", "Ljava/lang/ArithmeticException;", "Ljava/lang/ClassCastException;",
			"Ljava/lang/IllegalArgumentException;", "Ljava/lang/IllegalStateException;", "Ljava/lang/SecurityException;", "Ljava/lang/IndexOutOfBoundsException;", "Ljava/lang/ArrayIndexOutOfBoundsException;", "Ljava/lang/NegativeArraySizeException;"
		};
		string[] array = types;
		foreach (string type in array)
		{
			builder.Register(Api(type, "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
			{
				GuestThrowableMetadata.Set(RequireDex(args[0]), null, null);
				return null!;
			});
			builder.Register(Api(type, "<init>", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
			{
				GuestThrowableMetadata.Set(RequireDex(args[0]), RequireString(args[1], allowNull: true), null);
				return null!;
			});
			builder.Register(Api(type, "<init>", "(Ljava/lang/String;Ljava/lang/Throwable;)V"), delegate(AndroidApiInvocation _, object[] args)
			{
				GuestThrowableMetadata.Set(RequireDex(args[0]), RequireString(args[1], allowNull: true), args[2] as DexObject);
				return null!;
			});
		}
		builder.Register(Api("Ljava/lang/Throwable;", "getMessage", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => GuestThrowableMetadata.Message(RequireDex(args[0])));
		builder.Register(Api("Ljava/lang/Throwable;", "toString", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			DexObject dexObject = RequireDex(args[0]);
			string typeDescriptor = dexObject.TypeDescriptor;
			string text = typeDescriptor.Substring(1, typeDescriptor.Length - 1 - 1).Replace('/', '.');
			string text2 = GuestThrowableMetadata.Message(dexObject);
			return (text2 != null) ? (text + ": " + text2) : text;
		});
	}

	private static void RegisterSystemClock(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Landroid/os/SystemClock;", "uptimeMillis", "()J"), (AndroidApiInvocation _, object[] _) => state.Clock.UptimeMillis());
		builder.Register(Api("Landroid/os/SystemClock;", "elapsedRealtime", "()J"), (AndroidApiInvocation _, object[] _) => state.Clock.ElapsedRealtime());
		builder.Register(Api("Landroid/os/SystemClock;", "elapsedRealtimeNanos", "()J"), (AndroidApiInvocation _, object[] _) => state.Clock.ElapsedRealtimeNanos());
	}

	private static void RegisterLogs(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, IAndroidLogSink sink)
	{
		(string, int, string)[] array = new(string, int, string)[6]
		{
			("v", 2, "V"),
			("d", 3, "D"),
			("i", 4, "I"),
			("w", 5, "W"),
			("e", 6, "E"),
			("wtf", 7, "A")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, int, string) item = array[i];
			AndroidApiMethodId api = Api("Landroid/util/Log;", item.Item1, "(Ljava/lang/String;Ljava/lang/String;)I");
			builder.Register(api, (AndroidApiInvocation invocation, object[] args) => WriteLog(state, sink, invocation, args, item.Item2, item.Item3));
		}
		builder.Register(Api("Landroid/util/Log;", "println", "(ILjava/lang/String;Ljava/lang/String;)I"), (AndroidApiInvocation invocation, object[] args) => WriteLog(state, sink, invocation, new object[2]
		{
			args[1],
			args[2]
		}, RequireInt(args[0]), LevelFor(RequireInt(args[0]))));
		builder.Register(Api("Landroid/util/Log;", "isLoggable", "(Ljava/lang/String;I)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireString(args[0], allowNull: true);
			return (RequireInt(args[1]) >= state.MinimumLogPriority) ? 1 : 0;
		});
	}

	private static void RegisterText(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/lang/CharSequence;", "toString", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => AsText(state, args[0]));
		builder.Register(Api("Landroid/text/TextUtils;", "isEmpty", "(Ljava/lang/CharSequence;)Z"), (AndroidApiInvocation _, object[] args) => string.IsNullOrEmpty(AsText(state, args[0])) ? 1 : 0);
		builder.Register(Api("Landroid/text/TextUtils;", "equals", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Z"), (AndroidApiInvocation _, object[] args) => string.Equals(AsText(state, args[0]), AsText(state, args[1]), StringComparison.Ordinal) ? 1 : 0);
		builder.Register(Api("Landroid/text/TextUtils;", "getTrimmedLength", "(Ljava/lang/CharSequence;)I"), (AndroidApiInvocation _, object[] args) => JavaTrim(AsText(state, args[0]) ?? throw new AndroidApiNullReferenceException("TextUtils.getTrimmedLength receiver is null.")).Length);
	}

	private static void RegisterStrings(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/lang/String;", "length", "()I"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]).Length);
		builder.Register(Api("Ljava/lang/String;", "isEmpty", "()Z"), (AndroidApiInvocation _, object[] args) => (RequireString(args[0]).Length == 0) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "equals", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => (args[1] is string b && string.Equals(RequireString(args[0]), b, StringComparison.Ordinal)) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "equalsIgnoreCase", "(Ljava/lang/String;)Z"), (AndroidApiInvocation _, object[] args) => (args[1] is string right && JavaEqualsIgnoreCase(RequireString(args[0]), right)) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "startsWith", "(Ljava/lang/String;)Z"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]).StartsWith(RequireString(args[1]), StringComparison.Ordinal) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "endsWith", "(Ljava/lang/String;)Z"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]).EndsWith(RequireString(args[1]), StringComparison.Ordinal) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "contains", "(Ljava/lang/CharSequence;)Z"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]).Contains(AsText(state, args[1]) ?? throw new ArgumentException("String.contains argument is null."), StringComparison.Ordinal) ? 1 : 0);
		builder.Register(Api("Ljava/lang/String;", "indexOf", "(Ljava/lang/String;)I"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]).IndexOf(RequireString(args[1]), StringComparison.Ordinal));
		builder.Register(Api("Ljava/lang/String;", "indexOf", "(Ljava/lang/String;I)I"), (AndroidApiInvocation _, object[] args) => JavaIndexOf(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2])));
		builder.Register(Api("Ljava/lang/String;", "concat", "(Ljava/lang/String;)Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]) + RequireString(args[1]));
		builder.Register(Api("Ljava/lang/String;", "trim", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => JavaTrim(RequireString(args[0])));
		builder.Register(Api("Ljava/lang/String;", "toString", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => RequireString(args[0]));
		builder.Register(Api("Ljava/lang/String;", "hashCode", "()I"), (AndroidApiInvocation _, object[] args) => JavaHash(RequireString(args[0])));
		builder.Register(Api("Ljava/lang/String;", "valueOf", "(I)Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => RequireInt(args[0]).ToString(CultureInfo.InvariantCulture));
		builder.Register(Api("Ljava/lang/String;", "valueOf", "(Z)Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => (RequireInt(args[0]) == 0) ? "false" : "true");
		builder.Register(Api("Ljava/lang/String;", "valueOf", "(C)Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => ((char)RequireInt(args[0])).ToString());
	}

	private static void RegisterStringBuilder(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Ljava/lang/StringBuilder;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.StringBuilders.Add(Receiver(args), new StringBuilder());
			return null!;
		});
		builder.Register(Api("Ljava/lang/StringBuilder;", "<init>", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.StringBuilders.Add(Receiver(args), new StringBuilder(RequireInt(args[1])));
			return null!;
		});
		builder.Register(Api("Ljava/lang/StringBuilder;", "<init>", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.StringBuilders.Add(Receiver(args), new StringBuilder(RequireString(args[1])));
			return null!;
		});
		RegisterAppend(builder, state, "(Ljava/lang/String;)Ljava/lang/StringBuilder;", (object value) => (value == null) ? "null" : RequireString(value));
		RegisterAppend(builder, state, "(Ljava/lang/CharSequence;)Ljava/lang/StringBuilder;", (object value) => (value == null) ? "null" : AsText(state, value));
		RegisterAppend(builder, state, "(I)Ljava/lang/StringBuilder;", (object value) => RequireInt(value).ToString(CultureInfo.InvariantCulture));
		RegisterAppend(builder, state, "(Z)Ljava/lang/StringBuilder;", (object value) => (RequireInt(value) == 0) ? "false" : "true");
		RegisterAppend(builder, state, "(C)Ljava/lang/StringBuilder;", (object value) => ((char)RequireInt(value)).ToString());
		builder.Register(Api("Ljava/lang/StringBuilder;", "length", "()I"), (AndroidApiInvocation _, object[] args) => state.StringBuilders.Get(Receiver(args)).Length);
		builder.Register(Api("Ljava/lang/StringBuilder;", "toString", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => state.StringBuilders.Get(Receiver(args)).ToString());
	}

	private static void RegisterAppend(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string descriptor, Func<object, string> convert)
	{
		builder.Register(Api("Ljava/lang/StringBuilder;", "append", descriptor), delegate(AndroidApiInvocation _, object[] args)
		{
			DexObject dexObject = Receiver(args);
			state.StringBuilders.Get(dexObject).Append(convert(args[1]));
			return dexObject;
		});
	}

	private static void RegisterColor(AndroidApiRegistryBuilder builder)
	{
		builder.Register(Api("Landroid/graphics/Color;", "rgb", "(III)I"), (AndroidApiInvocation _, object[] args) => Pack(255, RequireInt(args[0]), RequireInt(args[1]), RequireInt(args[2])));
		builder.Register(Api("Landroid/graphics/Color;", "argb", "(IIII)I"), (AndroidApiInvocation _, object[] args) => Pack(RequireInt(args[0]), RequireInt(args[1]), RequireInt(args[2]), RequireInt(args[3])));
		builder.Register(Api("Landroid/graphics/Color;", "alpha", "(I)I"), (AndroidApiInvocation _, object[] args) => (RequireInt(args[0]) >>> 24) & 0xFF);
		builder.Register(Api("Landroid/graphics/Color;", "red", "(I)I"), (AndroidApiInvocation _, object[] args) => (RequireInt(args[0]) >>> 16) & 0xFF);
		builder.Register(Api("Landroid/graphics/Color;", "green", "(I)I"), (AndroidApiInvocation _, object[] args) => (RequireInt(args[0]) >>> 8) & 0xFF);
		builder.Register(Api("Landroid/graphics/Color;", "blue", "(I)I"), (AndroidApiInvocation _, object[] args) => RequireInt(args[0]) & 0xFF);
	}

	private static void RegisterBundles(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Landroid/os/Bundle;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Bundles.Add(Receiver(args), new BundlePeer());
			return null!;
		});
		builder.Register(Api("Landroid/os/Bundle;", "<init>", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireInt(args[1]);
			state.Bundles.Add(Receiver(args), new BundlePeer());
			return null!;
		});
		builder.Register(Api("Landroid/os/Bundle;", "<init>", "(Landroid/os/Bundle;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Bundles.Add(Receiver(args), state.Bundles.Get(RequireDex(args[1])).Copy());
			return null!;
		});
		RegisterBundlePut(builder, state, "putString", "(Ljava/lang/String;Ljava/lang/String;)V", BundleValueKind.String, (object value) => RequireString(value, allowNull: true));
		RegisterBundlePut(builder, state, "putInt", "(Ljava/lang/String;I)V", BundleValueKind.Int, (object value) => RequireInt(value));
		RegisterBundlePut(builder, state, "putLong", "(Ljava/lang/String;J)V", BundleValueKind.Long, (object value) => RequireLong(value));
		RegisterBundlePut(builder, state, "putBoolean", "(Ljava/lang/String;Z)V", BundleValueKind.Boolean, (object value) => RequireInt(value) != 0);
		RegisterBundleGet(builder, state, "getString", "(Ljava/lang/String;)Ljava/lang/String;", BundleValueKind.String, null);
		RegisterBundleGet(builder, state, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", BundleValueKind.String, 2);
		RegisterBundleGet(builder, state, "getInt", "(Ljava/lang/String;)I", BundleValueKind.Int, 0);
		RegisterBundleGet(builder, state, "getInt", "(Ljava/lang/String;I)I", BundleValueKind.Int, 2);
		RegisterBundleGet(builder, state, "getLong", "(Ljava/lang/String;)J", BundleValueKind.Long, 0L);
		RegisterBundleGet(builder, state, "getLong", "(Ljava/lang/String;J)J", BundleValueKind.Long, 2);
		RegisterBundleGet(builder, state, "getBoolean", "(Ljava/lang/String;)Z", BundleValueKind.Boolean, false);
		RegisterBundleGet(builder, state, "getBoolean", "(Ljava/lang/String;Z)Z", BundleValueKind.Boolean, 2);
		builder.Register(Api("Landroid/os/BaseBundle;", "containsKey", "(Ljava/lang/String;)Z"), (AndroidApiInvocation _, object[] args) => state.Bundles.Get(Receiver(args)).Contains(RequireString(args[1], allowNull: true)) ? 1 : 0);
		builder.Register(Api("Landroid/os/BaseBundle;", "remove", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Bundles.Get(Receiver(args)).Remove(RequireString(args[1], allowNull: true));
			return null!;
		});
		builder.Register(Api("Landroid/os/BaseBundle;", "clear", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Bundles.Get(Receiver(args)).Clear();
			return null!;
		});
		builder.Register(Api("Landroid/os/BaseBundle;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.Bundles.Get(Receiver(args)).Count);
		builder.Register(Api("Landroid/os/BaseBundle;", "isEmpty", "()Z"), (AndroidApiInvocation _, object[] args) => (state.Bundles.Get(Receiver(args)).Count == 0) ? 1 : 0);
	}

	private static void RegisterBundlePut(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string name, string descriptor, BundleValueKind kind, Func<object, object> convert)
	{
		builder.Register(Api("Landroid/os/BaseBundle;", name, descriptor), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Bundles.Get(Receiver(args)).Put(RequireString(args[1], allowNull: true), new BundleValue(kind, convert(args[2])));
			return null!;
		});
	}

	private static void RegisterBundleGet(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string name, string descriptor, BundleValueKind kind, object defaultValue)
	{
		builder.Register(Api("Landroid/os/BaseBundle;", name, descriptor), delegate(AndroidApiInvocation _, object[] args)
		{
			BundleValue bundleValue = state.Bundles.Get(Receiver(args)).Get(RequireString(args[1], allowNull: true));
			object obj;
			if (defaultValue is int)
			{
				int num = (int)defaultValue;
				if (num == 2)
				{
					obj = args[2];
					goto IL_004e;
				}
			}
			obj = defaultValue;
			goto IL_004e;
			IL_004e:
			object obj2 = obj;
			if ((object)bundleValue == null || bundleValue.Kind != kind)
			{
				return (kind == BundleValueKind.Boolean) ? ((object)RequireInt(obj2)) : obj2;
			}
			if (kind == BundleValueKind.String && bundleValue.Value == null && args.Length == 3)
			{
				return obj2;
			}
			return (kind == BundleValueKind.Boolean) ? ((object)(((bool)bundleValue.Value) ? 1 : 0)) : bundleValue.Value;
		});
	}

	private static void RegisterIntents(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Landroid/content/Intent;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Intents.Add(Receiver(args), new IntentPeer());
			return null!;
		});
		builder.Register(Api("Landroid/content/Intent;", "<init>", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Intents.Add(Receiver(args), new IntentPeer
			{
				Action = RequireString(args[1], allowNull: true)
			});
			return null!;
		});
		builder.Register(Api("Landroid/content/Intent;", "setAction", "(Ljava/lang/String;)Landroid/content/Intent;"), delegate(AndroidApiInvocation _, object[] args)
		{
			DexObject dexObject = Receiver(args);
			state.Intents.Get(dexObject).Action = RequireString(args[1], allowNull: true);
			return dexObject;
		});
		builder.Register(Api("Landroid/content/Intent;", "getAction", "()Ljava/lang/String;"), (AndroidApiInvocation _, object[] args) => state.Intents.Get(Receiver(args)).Action);
		RegisterIntentPut(builder, state, "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/Intent;", BundleValueKind.String, (object value) => RequireString(value, allowNull: true));
		RegisterIntentPut(builder, state, "(Ljava/lang/String;I)Landroid/content/Intent;", BundleValueKind.Int, (object value) => RequireInt(value));
		RegisterIntentPut(builder, state, "(Ljava/lang/String;Z)Landroid/content/Intent;", BundleValueKind.Boolean, (object value) => RequireInt(value) != 0);
		RegisterIntentGet(builder, state, "getStringExtra", "(Ljava/lang/String;)Ljava/lang/String;", BundleValueKind.String, null);
		RegisterIntentGet(builder, state, "getIntExtra", "(Ljava/lang/String;I)I", BundleValueKind.Int, 2);
		RegisterIntentGet(builder, state, "getBooleanExtra", "(Ljava/lang/String;Z)Z", BundleValueKind.Boolean, 2);
		builder.Register(Api("Landroid/content/Intent;", "hasExtra", "(Ljava/lang/String;)Z"), (AndroidApiInvocation _, object[] args) => state.Intents.Get(Receiver(args)).Extras.Contains(RequireString(args[1], allowNull: true)) ? 1 : 0);
		builder.Register(Api("Landroid/content/Intent;", "removeExtra", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Intents.Get(Receiver(args)).Extras.Remove(RequireString(args[1], allowNull: true));
			return null!;
		});
	}

	private static void RegisterToasts(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		builder.Register(Api("Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			if (!invocation.IsMainLane)
			{
				throw new AndroidApiUnavailableException(invocation.ResolvedApi, "Toast requires the runtime main lane.");
			}
			DexObject dexObject = RequireDex(args[0]);
			if (dexObject != state.ApplicationContext && dexObject != state.Activity)
			{
				throw new ArgumentException("Toast context does not belong to this session.");
			}
			string text = AsText(state, args[1]) ?? string.Empty;
			if (text.Length > state.ToastLimits.MaxTextLength)
			{
				throw new ArgumentOutOfRangeException("args", $"Toast text exceeds {state.ToastLimits.MaxTextLength} characters.");
			}
			int duration = RequireToastDuration(args[2]);
			DexObject activity = state.Activity ?? throw new InvalidOperationException("Session Activity is not attached.");
			IActivityWindow activityWindow = RequireWindow(state, activity);
			IAndroidToastHost toastHost = activityWindow as IAndroidToastHost;
			if (toastHost == null)
			{
				throw new AndroidApiUnavailableException(invocation.ResolvedApi, "Activity window does not provide a text Toast host.");
			}
			DexObject dexObject2 = new DexObject("Landroid/widget/Toast;");
			state.Toasts.AddCreated(dexObject2, () => new ToastPeer
			{
				Notification = toastHost.CreateToast(text, duration, invocation.CancellationToken)
			});
			return dexObject2;
		});
		builder.Register(Api("Landroid/widget/Toast;", "show", "()V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			state.Toasts.Get(Receiver(args)).Notification.Show(invocation.CancellationToken);
			return null!;
		});
		builder.Register(Api("Landroid/widget/Toast;", "cancel", "()V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			state.Toasts.Get(Receiver(args)).Notification.Cancel();
			return null!;
		});
		builder.Register(Api("Landroid/widget/Toast;", "getDuration", "()I"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.Toasts.Get(Receiver(args)).Notification.Duration;
		});
		builder.Register(Api("Landroid/widget/Toast;", "setDuration", "(I)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			state.Toasts.Get(Receiver(args)).Notification.Duration = RequireToastDuration(args[1]);
			return null!;
		});
		builder.Register(Api("Landroid/widget/Toast;", "setText", "(Ljava/lang/CharSequence;)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			string text = AsText(state, args[1]) ?? string.Empty;
			if (text.Length > state.ToastLimits.MaxTextLength)
			{
				throw new ArgumentOutOfRangeException("args", $"Toast text exceeds {state.ToastLimits.MaxTextLength} characters.");
			}
			state.Toasts.Get(Receiver(args)).Notification.Text = text;
			return null!;
		});
	}

	private static void RegisterIntentPut(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string descriptor, BundleValueKind kind, Func<object, object> convert)
	{
		builder.Register(Api("Landroid/content/Intent;", "putExtra", descriptor), delegate(AndroidApiInvocation _, object[] args)
		{
			DexObject dexObject = Receiver(args);
			state.Intents.Get(dexObject).Extras.Put(RequireString(args[1], allowNull: true), new BundleValue(kind, convert(args[2])));
			return dexObject;
		});
	}

	private static void RegisterIntentGet(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string name, string descriptor, BundleValueKind kind, object defaultValue)
	{
		builder.Register(Api("Landroid/content/Intent;", name, descriptor), delegate(AndroidApiInvocation _, object[] args)
		{
			BundleValue bundleValue = state.Intents.Get(Receiver(args)).Extras.Get(RequireString(args[1], allowNull: true));
			object obj;
			if (defaultValue is int)
			{
				int num = (int)defaultValue;
				if (num == 2)
				{
					obj = args[2];
					goto IL_0053;
				}
			}
			obj = defaultValue;
			goto IL_0053;
			IL_0053:
			object obj2 = obj;
			if ((object)bundleValue == null || bundleValue.Kind != kind)
			{
				return (kind == BundleValueKind.Boolean) ? ((object)RequireInt(obj2)) : obj2;
			}
			return (kind == BundleValueKind.Boolean) ? ((object)(((bool)bundleValue.Value) ? 1 : 0)) : bundleValue.Value;
		});
	}

	private static object SetActivityTitle(AndroidFrameworkState state, AndroidApiInvocation invocation, object[] args)
	{
		DexObject activity = Receiver(args);
		string title = AsText(state, args[1]);
		if (!state.WindowPeers.TryGet(activity, out IActivityWindow window))
		{
			throw new AndroidApiUnavailableException(SetTitle, "Activity window peer is not available.");
		}
		try
		{
			window.SetTitle(title, invocation.CancellationToken);
			return null!;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new AndroidApiUnavailableException(SetTitle, "Activity window dispatcher is unavailable.", innerException);
		}
	}

	private static int WriteLog(AndroidFrameworkState state, IAndroidLogSink sink, AndroidApiInvocation invocation, object[] args, int priority, string level)
	{
		string tag = RequireString(args[0], allowNull: true);
		string message = RequireString(args[1]);
		if (priority < state.MinimumLogPriority)
		{
			return 0;
		}
		int result = sink.Info(new AndroidLogEntry(invocation.SessionId, invocation.PackageName, invocation.ActivityDescriptor, tag, message, invocation, priority, level));
		if (result <= 0)
		{
			throw new InvalidOperationException("Accepted Android log entries must return a positive value.");
		}
		return result;
	}

	private static IActivityWindow RequireWindow(AndroidFrameworkState state, DexObject activity)
	{
		if (!state.WindowPeers.TryGet(activity, out IActivityWindow window))
		{
			throw new AndroidApiUnavailableException(SetTitle, "Activity window peer is not available.");
		}
		return window;
	}

	private static DexObject RequireActivity(AndroidFrameworkState state, DexObject value)
	{
		if (value != state.Activity)
		{
			throw new ArgumentException("Activity receiver does not belong to this session.");
		}
		return value;
	}

	private static void RequireContext(AndroidFrameworkState state, DexObject value)
	{
		if (value != state.Activity && value != state.ApplicationContext)
		{
			throw new ArgumentException("Context receiver does not belong to this session.");
		}
	}

	private static string LocalClassName(AndroidFrameworkState state, DexObject activity)
	{
		string name = activity.TypeDescriptor.TrimStart('L').TrimEnd(';').Replace('/', '.');
		string prefix = state.PackageName + ".";
		string result;
		if (!name.StartsWith(prefix, StringComparison.Ordinal))
		{
			result = name;
		}
		else
		{
			string text = name;
			int length = prefix.Length;
			result = text.Substring(length, text.Length - length);
		}
		return result;
	}

	private static string AsText(AndroidFrameworkState state, object value)
	{
		if (1 == 0)
		{
		}
		string result;
		if (value != null)
		{
			if (!(value is string text))
			{
				if (!(value is DexObject guest) || state == null || !(guest.TypeDescriptor == "Ljava/lang/StringBuilder;"))
				{
					throw new ArgumentException("Expected CharSequence string or StringBuilder.");
				}
				result = state.StringBuilders.Get(guest).ToString();
			}
			else
			{
				result = text;
			}
		}
		else
		{
			result = null;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static DexObject Receiver(object[] args)
	{
		return RequireDex(args[0]);
	}

	private static DexObject RequireDex(object value)
	{
		return (value as DexObject) ?? throw new ArgumentException("Expected DEX object.");
	}

	private static DexObject OptionalDex(object value)
	{
		return (value == null) ? null : RequireDex(value);
	}

	private static AndroidUiSession RequireUi(AndroidFrameworkState state)
	{
		return state.Ui ?? throw new AndroidApiUnavailableException(new AndroidApiMethodId("Landroid/app/Activity;", "setContentView", "(I)V"), "APK resource/UI session is unavailable.");
	}

	private static string RequireString(object value, bool allowNull = false)
	{
		object obj = value as string;
		if (obj == null)
		{
			if (!allowNull || value != null)
			{
				throw new ArgumentException("Expected string.");
			}
			obj = null;
		}
		return (string)obj;
	}

	private static int RequireInt(object value)
	{
		if (1 == 0)
		{
		}
		int result;
		if (!(value is int number))
		{
			if (value is bool)
			{
				result = (((bool)value) ? 1 : 0);
			}
			else
			{
				if (!(value is char character))
				{
					throw new ArgumentException("Expected int-compatible value.");
				}
				result = character;
			}
		}
		else
		{
			result = number;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static long RequireLong(object value)
	{
		if (value is long)
		{
			return (long)value;
		}
		throw new ArgumentException("Expected long value.");
	}

	private static int JavaHash(string value)
	{
		int hash = 0;
		foreach (char c in value)
		{
			hash = hash * 31 + c;
		}
		return hash;
	}

	private static bool JavaEqualsIgnoreCase(string left, string right)
	{
		if (left.Length != right.Length)
		{
			return false;
		}
		int leftIndex = 0;
		int rightIndex = 0;
		while (leftIndex < left.Length && rightIndex < right.Length)
		{
			int a = ReadJavaCodePoint(left, ref leftIndex);
			int b = ReadJavaCodePoint(right, ref rightIndex);
			if (a != b)
			{
				int upperA = JavaUpper(a);
				int upperB = JavaUpper(b);
				if (upperA != upperB && JavaLower(upperA) != JavaLower(upperB))
				{
					return false;
				}
			}
		}
		return leftIndex == left.Length && rightIndex == right.Length;
	}

	private static int ReadJavaCodePoint(string value, ref int index)
	{
		char first = value[index++];
		if (char.IsHighSurrogate(first) && index < value.Length && char.IsLowSurrogate(value[index]))
		{
			return char.ConvertToUtf32(first, value[index++]);
		}
		return first;
	}

	private static int JavaUpper(int value)
	{
		if (1 == 0)
		{
		}
		int result;
		if (value >= 66928)
		{
			if (value <= 67004)
			{
				result = value;
				goto IL_0093;
			}
		}
		else if (value >= 55296)
		{
			if (value <= 57343)
			{
				result = value;
				goto IL_0093;
			}
		}
		else if (value <= 42945)
		{
			if (value == 305)
			{
				result = 73;
				goto IL_0093;
			}
			if (value == 11359 || value == 42945)
			{
				goto IL_0071;
			}
		}
		else if (value == 42961 || value == 42967 || value == 42969)
		{
			goto IL_0071;
		}
		result = Rune.ToUpperInvariant(new Rune(value)).Value;
		goto IL_0093;
		IL_0071:
		result = value;
		goto IL_0093;
		IL_0093:
		if (1 == 0)
		{
		}
		return result;
	}

	private static int JavaLower(int value)
	{
		if (1 == 0)
		{
		}
		int result;
		if (value >= 66928)
		{
			if (value <= 67004)
			{
				result = value;
				goto IL_0093;
			}
		}
		else if (value >= 55296)
		{
			if (value <= 57343)
			{
				result = value;
				goto IL_0093;
			}
		}
		else if (value <= 42944)
		{
			if (value == 304)
			{
				result = 105;
				goto IL_0093;
			}
			if (value == 11311 || value == 42944)
			{
				goto IL_0071;
			}
		}
		else if (value == 42960 || value == 42966 || value == 42968)
		{
			goto IL_0071;
		}
		result = Rune.ToLowerInvariant(new Rune(value)).Value;
		goto IL_0093;
		IL_0071:
		result = value;
		goto IL_0093;
		IL_0093:
		if (1 == 0)
		{
		}
		return result;
	}

	private static string JavaTrim(string value)
	{
		int start = 0;
		int end;
		for (end = value.Length; start < end && value[start] <= ' '; start++)
		{
		}
		while (end > start && value[end - 1] <= ' ')
		{
			end--;
		}
		int num = start;
		return value.Substring(num, end - num);
	}

	private static int JavaIndexOf(string value, string search, int from)
	{
		return value.IndexOf(search, Math.Clamp(from, 0, value.Length), StringComparison.Ordinal);
	}

	private static int Pack(int a, int r, int g, int b)
	{
		return (a << 24) | (r << 16) | (g << 8) | b;
	}

	private static int RequireToastDuration(object value)
	{
		int duration = RequireInt(value);
		if ((uint)duration > 1u)
		{
			throw new ArgumentOutOfRangeException("value", "Toast duration must be LENGTH_SHORT (0) or LENGTH_LONG (1).");
		}
		return duration;
	}

	private static void RequireMainLane(AndroidApiInvocation invocation)
	{
		if (!invocation.IsMainLane)
		{
			throw new AndroidApiUnavailableException(invocation.ResolvedApi, "Toast requires the runtime main lane.");
		}
	}

	private static string LevelFor(int priority)
	{
		if (1 == 0)
		{
		}
		string result = priority switch
		{
			2 => "V", 
			3 => "D", 
			4 => "I", 
			5 => "W", 
			6 => "E", 
			7 => "A", 
			_ => "?", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static AndroidApiMethodId Api(string owner, string name, string descriptor)
	{
		return new AndroidApiMethodId(owner, name, descriptor);
	}

	private static void RegisterVoid(AndroidApiRegistryBuilder builder, string owner, string name, string descriptor)
	{
		builder.Register(owner, name, descriptor, (AndroidApiInvocation _, object[] _) => (object)null);
	}
}
