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
using AndroidRuntime.Core.Apk;

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
		JavaLangStringBindings.Register(builder, state);
		JavaLangReflectBindings.Register(builder, state);
		JavaLangBoxingBindings.Register(builder, state);
		KotlinTextStringsKtBindings.Register(builder, state);
		KotlinLazyBindings.Register(builder, state);
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
		// ComponentActivity/Activity.getApplication() -> the session Application.
		builder.Register(Api("Landroid/app/Activity;", "getApplication", "()Landroid/app/Application;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return state.ApplicationContext;
		});
		// Application.registerActivityLifecycleCallbacks: the runtime drives the
		// lifecycle itself; accepted as a no-op (no callbacks are dispatched).
		builder.Register(Api("Landroid/app/Application;", "registerActivityLifecycleCallbacks", "(Landroid/app/Application$ActivityLifecycleCallbacks;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// Some androidx paths call the same registration through an Activity
		// receiver; same no-op.
		builder.Register(Api("Landroid/app/Activity;", "registerActivityLifecycleCallbacks", "(Landroid/app/Application$ActivityLifecycleCallbacks;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// getLastNonConfigurationInstance(): the runtime never performs a
		// configuration-change recreation, so null is the honest answer.
		builder.Register(Api("Landroid/app/Activity;", "getLastNonConfigurationInstance", "()Ljava/lang/Object;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return null!;
		});
		// Activity.getFragmentManager(): the legacy framework fragment manager is
		// not hosted (apps on this runtime use androidx fragments); a stable facade
		// lets lifecycle-reporting probes proceed while fragment ops stay unbound.
		builder.Register(Api("Landroid/app/Activity;", "getFragmentManager", "()Landroid/app/FragmentManager;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return state.FragmentManagerObject;
		});
		builder.Register(Api("Landroid/app/FragmentManager;", "findFragmentByTag", "(Ljava/lang/String;)Landroid/app/Fragment;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/app/FragmentManager;", "executePendingTransactions", "()Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		// ReportFragment self-injection: beginTransaction/add/commit are accepted
		// as a no-op transaction (lifecycle reporting is host-driven, so the
		// fragment never needs to actually install).
		builder.Register(Api("Landroid/app/FragmentManager;", "beginTransaction", "()Landroid/app/FragmentTransaction;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.FragmentTransactionObject;
		});
		builder.Register(Api("Landroid/app/FragmentTransaction;", "add", "(Landroid/app/Fragment;Ljava/lang/String;)Landroid/app/FragmentTransaction;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.FragmentTransactionObject;
		});
		builder.Register(Api("Landroid/app/FragmentTransaction;", "commit", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		// ReportFragment.<init> chain reaches the framework Fragment constructor;
		// the fragment is never actually installed, so no peer state is needed.
		builder.Register(Api("Landroid/app/Fragment;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		RegisterLogs(builder, state, logSink);
		RegisterText(builder, state);
		RegisterStringBuilder(builder, state);
		RegisterColor(builder);
		RegisterSystemClock(builder, state);
		RegisterThreadLocalRandom(builder, state);
		builder.Register(Api("Ljava/lang/Object;", "clone", "()Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return args[0] switch
			{
				DexArray array => array.Clone(),
				_ => throw new ArgumentException("Object.clone is only supported for arrays in this runtime.")
			};
		});
		RegisterThrowables(builder);
		AndroidSystemServiceBindings.Register(builder, state);
		RegisterBundles(builder, state);
		RegisterIntents(builder, state);
		RegisterToasts(builder, state);
		JavaUtilConcurrentAtomicBindings.Register(builder, state);
		JavaUtilConcurrentBindings.Register(builder, state);
		JavaUtilMapBindings.Register(builder, state);
		JavaUtilCollectionsBindings.Register(builder, state);
		JavaUtilArrayDequeBindings.Register(builder, state);
		JavaUtilLinkedHashSetBindings.Register(builder, state);
		JavaUtilLinkedHashMapBindings.Register(builder, state);
		AndroidContentSharedPreferencesBindings.Register(builder, state);
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
		builder.Register(Api("Ljava/util/WeakHashMap;", "<init>", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireInt(args[1]);
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
		builder.Register(Api("Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => { state.HashMaps.Get(Receiver(args)).RequireMutable(); return state.HashMaps.Get(Receiver(args)).Put(args[1], args[2]); });
		builder.Register(Api("Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Get(args[1]));
		builder.Register(Api("Ljava/util/HashMap;", "containsKey", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).ContainsKey(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/HashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), (AndroidApiInvocation _, object[] args) => { state.HashMaps.Get(Receiver(args)).RequireMutable(); return state.HashMaps.Get(Receiver(args)).Remove(args[1]); });
		builder.Register(Api("Ljava/util/HashMap;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.HashMaps.Get(Receiver(args)).Count);
	}

	private static void RegisterArrayLists(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		// AbstractList.<init>()V is invoked by real subclass constructors
		// (ArrayList etc. call super()); the state lives in the concrete peer, so
		// the abstract constructor is a no-op.
		builder.Register(Api("Ljava/util/AbstractList;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
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
			state.ArrayLists.Get(Receiver(args)).RequireMutable();
			state.ArrayLists.Get(Receiver(args)).Elements.Add(args[1]);
			return 1;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "add", "(ILjava/lang/Object;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			listPeer.RequireMutable();
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
			listPeer.RequireMutable();
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
			state.ArrayLists.Get(Receiver(args)).RequireMutable();
			state.ArrayLists.Get(Receiver(args)).Elements.Clear();
			return null!;
		});
		builder.Register(Api("Ljava/util/ArrayList;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.ArrayLists.Get(Receiver(args)).Elements.Count);
		builder.Register(Api("Ljava/util/ArrayList;", "isEmpty", "()Z"), (AndroidApiInvocation _, object[] args) => (state.ArrayLists.Get(Receiver(args)).Elements.Count == 0) ? 1 : 0);
		builder.Register(Api("Ljava/util/ArrayList;", "remove", "(I)Ljava/lang/Object;"), delegate(AndroidApiInvocation _, object[] args)
		{
			ListPeer listPeer = state.ArrayLists.Get(Receiver(args));
			listPeer.RequireMutable();
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
		// java.util.HashSet: same bounded HashSet<object?> peer semantics (no
		// ordering guarantees, set semantics only) — shares the store so the
		// runtime's set behavior is uniform across both classes.
		builder.Register(Api("Ljava/util/HashSet;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.CopyOnWriteArraySets.Add(Receiver(args), new HashSet<object>());
			return null!;
		});
		builder.Register(Api("Ljava/util/HashSet;", "add", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArraySets.Get(Receiver(args)).Add(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/HashSet;", "addAll", "(Ljava/util/Collection;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			var peer = state.CopyOnWriteArraySets.Get(Receiver(args));
			bool changed = false;
			foreach (object item in Bindings.JavaUtilLinkedHashSetBindings.CollectionItems(state, RequireDex(args[1])))
				changed |= peer.Add(item!);
			return changed ? 1 : 0;
		});
		builder.Register(Api("Ljava/util/HashSet;", "contains", "(Ljava/lang/Object;)Z"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArraySets.Get(Receiver(args)).Contains(args[1]) ? 1 : 0);
		builder.Register(Api("Ljava/util/HashSet;", "size", "()I"), (AndroidApiInvocation _, object[] args) => state.CopyOnWriteArraySets.Get(Receiver(args)).Count);
		builder.Register(Api("Ljava/util/HashSet;", "iterator", "()Ljava/util/Iterator;"), (AndroidApiInvocation _, object[] args) => CreateIterator(state, state.CopyOnWriteArraySets.Get(Receiver(args))));
		// java.util.Collection.removeAll on a Collection receiver: dispatch to the
		// appropriate peer (HashSet/CopyOnWriteArraySet share the set store;
		// ArrayList/CopyOnWriteArrayList share the list store). Interface-typed
		// call sites resolve here when the runtime has no guest implementation.
		builder.Register(Api("Ljava/util/Collection;", "removeAll", "(Ljava/util/Collection;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			var items = Bindings.JavaUtilLinkedHashSetBindings.CollectionItems(state, RequireDex(args[1])).ToList();
			if (state.CopyOnWriteArraySets.TryGet(Receiver(args), out var set))
			{
				bool changed = false;
				foreach (object item in items) changed |= set.Remove(item);
				return changed ? 1 : 0;
			}
			if (state.MapViews.TryGet(Receiver(args), out var view))
			{
				bool changed = false;
				foreach (object item in items) changed |= view.Remove(item);
				return changed ? 1 : 0;
			}
			if (state.ArrayLists.TryGet(Receiver(args), out var list))
			{
				list.RequireMutable();
				int before = list.Elements.Count;
				list.Elements.RemoveAll(items.Contains);
				return before != list.Elements.Count ? 1 : 0;
			}
			throw new InvalidOperationException("Collection receiver has no bound peer: " + Receiver(args).TypeDescriptor);
		});
		builder.Register(Api("Ljava/util/Collection;", "remove", "(Ljava/lang/Object;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			if (state.CopyOnWriteArraySets.TryGet(Receiver(args), out var set))
				return set.Remove(args[1]) ? 1 : 0;
			if (state.ArrayLists.TryGet(Receiver(args), out var list))
			{
				list.RequireMutable();
				return list.Elements.Remove(args[1]) ? 1 : 0;
			}
			throw new InvalidOperationException("Collection receiver has no bound peer: " + Receiver(args).TypeDescriptor);
		});
		builder.Register(Api("Ljava/util/Collection;", "addAll", "(Ljava/util/Collection;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			var items = Bindings.JavaUtilLinkedHashSetBindings.CollectionItems(state, RequireDex(args[1])).ToList();
			if (state.CopyOnWriteArraySets.TryGet(Receiver(args), out var set))
			{
				bool changed = false;
				foreach (object item in items) changed |= set.Add(item);
				return changed ? 1 : 0;
			}
			if (state.ArrayLists.TryGet(Receiver(args), out var list))
			{
				list.RequireMutable();
				int before = list.Elements.Count;
				list.Elements.AddRange(items);
				return before != list.Elements.Count ? 1 : 0;
			}
			throw new InvalidOperationException("Collection receiver has no bound peer: " + Receiver(args).TypeDescriptor);
		});
		builder.Register(Api("Ljava/util/Collection;", "clear", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			if (state.CopyOnWriteArraySets.TryGet(Receiver(args), out var set)) { set.Clear(); return null!; }
			if (state.ArrayLists.TryGet(Receiver(args), out var list)) { list.RequireMutable(); list.Elements.Clear(); return null!; }
			throw new InvalidOperationException("Collection receiver has no bound peer: " + Receiver(args).TypeDescriptor);
		});
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
		builder.Register(Api("Landroid/view/LayoutInflater;", "from", "(Landroid/content/Context;)Landroid/view/LayoutInflater;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, RequireDex(args[0]));
			return state.LayoutInflaterObject;
		});
		builder.Register(Api("Landroid/view/LayoutInflater;", "inflate", "(I)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).Inflate(RequireInt(args[1]));
		});
		builder.Register(Api("Landroid/view/LayoutInflater;", "inflate", "(ILandroid/view/ViewGroup;Z)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).Inflate(RequireInt(args[1]));
		});
		builder.Register(Api("Landroid/view/LayoutInflater;", "inflate", "(ILandroid/view/ViewGroup;)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).Inflate(RequireInt(args[1]));
		});
		// The runtime never installs a LayoutInflater.Factory (custom view inflation
		// is a host-side concept here), so getFactory() legitimately returns null —
		// appcompat calls it defensively and falls back to its own path.
		builder.Register(Api("Landroid/view/LayoutInflater;", "getFactory", "()Landroid/view/LayoutInflater$Factory;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// appcompat calls setFactory2 with its inflation bridge. Custom factories
		// cannot participate in the runtime's native inflater, so the call is a
		// documented no-op: accepting it keeps appcompat on its normal path while
		// layout inflation stays entirely host-side.
		builder.Register(Api("Landroid/view/LayoutInflater;", "setFactory2", "(Landroid/view/LayoutInflater$Factory2;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// appcompat night-mode plumbing: no night mode was ever configured, so the
		// honest default is MODE_NIGHT_FOLLOW_SYSTEM (-1) — appcompat then uses the
		// system setting, which the runtime reports as light mode.
		builder.Register(Api("Landroidx/appcompat/app/AppCompatDelegateImpl;", "getDefaultNightMode", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return -1;
		});
		// AppCompatDelegateImpl.addActiveDelegate is a static registry bookkeeping
		// call; accepting it keeps the delegate lifecycle moving (no observable
		// behavior for this runtime).
		builder.Register(Api("Landroidx/appcompat/app/AppCompatDelegateImpl;", "addActiveDelegate", "(Landroidx/appcompat/app/AppCompatDelegate;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/content/Context;", "startService", "(Landroid/content/Intent;)Landroid/content/ComponentName;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			var service = new DexObject("Landroid/content/ComponentName;");
			string action = state.Intents.TryGet(RequireDex(args[1]), out var peer) ? peer.Action ?? string.Empty : string.Empty;
			state.SystemServices?.Audit("service." + action, "startService", true, 0, 0, null);
			return service;
		});
		// Same semantics as startService (no real service model): audited, honest
		// ComponentName facade. A foreground service's startForeground obligation
		// cannot exist without a service, so the call is a bounded no-op.
		builder.Register(Api("Landroid/content/Context;", "startForegroundService", "(Landroid/content/Intent;)Landroid/content/ComponentName;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			var service = new DexObject("Landroid/content/ComponentName;");
			string action = state.Intents.TryGet(RequireDex(args[1]), out var peer) ? peer.Action ?? string.Empty : string.Empty;
			state.SystemServices?.Audit("service." + action, "startForegroundService", true, 0, 0, null);
			return service;
		});
		// Context.getResources() returns the stable per-session Resources facade;
		// reads resolve through the APK resource table.
		builder.Register(Api("Landroid/content/Context;", "getResources", "()Landroid/content/res/Resources;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.Resources is null
				? throw new AndroidApiUnavailableException(invocation.ResolvedApi, "APK resource/UI session is unavailable.")
				: state.ResourcesObject;
		});
		// Activity.runOnUiThread(Runnable): executes inline when already on the
		// main lane, otherwise enqueues onto the main Looper (real semantics).
		builder.Register(Api("Landroid/app/Activity;", "runOnUiThread", "(Ljava/lang/Runnable;)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			var runnable = RequireDex(args[1]);
			if (invocation.IsMainLane)
			{
				state.Interpreter?.InvokeInstanceExact(runnable, "run", "()V");
			}
			else
			{
				Bindings.AndroidOsHandlerBindings.PostPublic(state, runnable);
			}
			return null!;
		});
		builder.Register(Api("Landroid/content/res/Configuration;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// Configuration(Configuration): copies the source's public fields so the
		// copy reads identically through iget.
		builder.Register(Api("Landroid/content/res/Configuration;", "<init>", "(Landroid/content/res/Configuration;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			var target = RequireDex(args[0]);
			var source = RequireDex(args[1]);
			foreach (var pair in source.InstanceFields)
				target.InstanceFields[pair.Key] = pair.Value;
			return null!;
		});
		// Configuration.getLocales() -> a one-locale LocaleList facade; the locale
		// reads as the en-US default (see getConfiguration). LocaleList.get(0)
		// returns the Locale object the app can then interrogate.
		builder.Register(Api("Landroid/content/res/Configuration;", "getLocales", "()Landroid/os/LocaleList;"), delegate(AndroidApiInvocation _, object[] args)
		{
			var list = new DexObject("Landroid/os/LocaleList;");
			list.InstanceFields["Landroid/os/LocaleList;->_locale:Ljava/util/Locale;"] = state.LocaleObject;
			return list;
		});
		// LocaleList(Locale[]): the app constructs its own list; retain the array so
		// get/isEmpty answer from the app's own locales.
		builder.Register(Api("Landroid/os/LocaleList;", "<init>", "([Ljava/util/Locale;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireDex(args[0]).InstanceFields["_array"] = args[1] as DexArray;
			return null!;
		});
		builder.Register(Api("Landroid/os/LocaleList;", "isEmpty", "()Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			var list = RequireDex(args[0]);
			return list.InstanceFields.TryGetValue("_array", out var array) && array is DexArray dexArray && dexArray.Length == 0 ? 1 : 0;
		});
		builder.Register(Api("Landroid/os/LocaleList;", "get", "(I)Ljava/util/Locale;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			int index = RequireInt(args[1]);
			var list = RequireDex(args[0]);
			if (list.InstanceFields.TryGetValue("_array", out var array) && array is DexArray dexArray)
				return index >= 0 && index < dexArray.Length ? dexArray.Get(index) ?? null! : null!;
			return index == 0 ? state.LocaleObject : null!;
		});
		builder.Register(Api("Landroid/os/LocaleList;", "toLanguageTags", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return "en-US";
		});
		builder.Register(Api("Ljava/util/Locale;", "getLanguage", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireDex(args[0]).InstanceFields.TryGetValue("_language", out var language) && language is string text ? text : "en";
		});
		// Locale(String, String): the app constructs a locale; its language/country
		// args are retained so the getters answer from the app's own values.
		builder.Register(Api("Ljava/util/Locale;", "<init>", "(Ljava/lang/String;Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			var locale = RequireDex(args[0]);
			locale.InstanceFields["_language"] = RequireString(args[1], allowNull: true);
			locale.InstanceFields["_country"] = args.Length > 2 ? RequireString(args[2], allowNull: true) : null;
			return null!;
		});
		builder.Register(Api("Ljava/util/Locale;", "<init>", "(Ljava/lang/String;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			RequireDex(args[0]).InstanceFields["_language"] = RequireString(args[1], allowNull: true);
			return null!;
		});
		// Locale.forLanguageTag("en-US"): static; parses the tag into language/country.
		builder.Register(Api("Ljava/util/Locale;", "forLanguageTag", "(Ljava/lang/String;)Ljava/util/Locale;"), delegate(AndroidApiInvocation _, object[] args)
		{
			string tag = RequireString(args[0], allowNull: true) ?? string.Empty;
			var locale = new DexObject("Ljava/util/Locale;");
			int dash = tag.IndexOf('-');
			locale.InstanceFields["_language"] = dash < 0 ? tag : tag.Substring(0, dash);
			locale.InstanceFields["_country"] = dash < 0 ? string.Empty : tag.Substring(dash + 1);
			return locale;
		});
		builder.Register(Api("Ljava/util/Locale;", "getCountry", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireDex(args[0]).InstanceFields.TryGetValue("_country", out var country) && country is string text ? text : "US";
		});
		builder.Register(Api("Ljava/util/Locale;", "toString", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			var locale = RequireDex(args[0]);
			string language = locale.InstanceFields.TryGetValue("_language", out var lang) && lang is string lt ? lt : "en";
			string country = locale.InstanceFields.TryGetValue("_country", out var ctr) && ctr is string ct ? ct : "US";
			return country.Length == 0 ? language : language + "_" + country;
		});
		RegisterResources(builder, state);
		RegisterTypedArray(builder, state);
		// No service binding exists in this runtime: bindService honestly returns
		// false (no connection established). The ServiceConnection is never invoked.
		builder.Register(Api("Landroid/content/Context;", "bindService", "(Landroid/content/Intent;Landroid/content/ServiceConnection;I)Z"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			string action = state.Intents.TryGet(RequireDex(args[1]), out var peer) ? peer.Action ?? string.Empty : string.Empty;
			state.SystemServices?.Audit("service." + action, "bindService", true, 0, 0, null);
			return 0;
		});
		builder.Register(Api("Landroid/app/Activity;", "findViewById", "(I)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return RequireUi(state).FindViewById(RequireInt(args[1]));
		});
		// Activity.getWindow(): the stable per-session Window facade appcompat
		// requires; the real content plumbing stays in the UI session.
		builder.Register(Api("Landroid/app/Activity;", "getWindow", "()Landroid/view/Window;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return state.WindowObject;
		});
		// Window.setContentView(int) mirrors Activity.setContentView.
		builder.Register(Api("Landroid/view/Window;", "setContentView", "(I)V"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireUi(state).SetContentView(RequireInt(args[1]));
			return null!;
		});
		// Window.setContentView(View): the runtime content model is layout-resource
		// based; a programmatic View content is out of scope (fail closed).
		builder.Register(Api("Landroid/view/Window;", "setContentView", "(Landroid/view/View;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// Window.setCallback(Window.Callback): appcompat registers its delegate as
		// the window callback; retain it so getCallback() answers non-null.
		builder.Register(Api("Landroid/view/Window;", "setCallback", "(Landroid/view/Window$Callback;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.WindowCallback = OptionalDex(args[1]);
			return null!;
		});
		// Window.getCallback(): returns the retained callback; real Android
		// initializes the window callback to the Activity itself (Activity.attach
		// calls setCallback), so the Activity is the default until appcompat
		// replaces it.
		builder.Register(Api("Landroid/view/Window;", "getCallback", "()Landroid/view/Window$Callback;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.WindowCallback ?? state.Activity ?? null!;
		});
		// Window.getDecorView(): a stable DecorView facade (a FrameLayout); content
		// still lives in the UI session.
		builder.Register(Api("Landroid/view/Window;", "getDecorView", "()Landroid/view/View;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.DecorViewObject;
		});
		builder.Register(Api("Landroid/view/Window;", "getDecorView", "()Landroid/view/ViewGroup;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.DecorViewObject;
		});
		builder.Register(Api("Landroid/view/Window;", "findViewById", "(I)Landroid/view/View;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return RequireUi(state).FindViewById(RequireInt(args[1]));
		});
		// Activity.getOnBackInvokedDispatcher(): stable facade; back navigation is
		// host-driven, so the app can register but nothing is dispatched here.
		builder.Register(Api("Landroid/app/Activity;", "getOnBackInvokedDispatcher", "()Landroid/window/OnBackInvokedDispatcher;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return state.OnBackInvokedDispatcherObject;
		});
		// Activity.getComponentName(): the session's own package + activity class.
		builder.Register(Api("Landroid/app/Activity;", "getComponentName", "()Landroid/content/ComponentName;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			RequireActivity(state, Receiver(args));
			return state.EnsureComponentName(state.Activity!.TypeDescriptor);
		});
		builder.Register(Api("Landroid/window/OnBackInvokedDispatcher;", "registerOnBackInvokedCallback", "(ILandroid/window/OnBackInvokedCallback;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/window/OnBackInvokedDispatcher;", "unregisterOnBackInvokedCallback", "(Landroid/window/OnBackInvokedCallback;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/view/Window;", "setDecorFitsSystemWindows", "(Z)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/view/Window;", "addFlags", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/view/Window;", "setStatusBarColor", "(I)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/view/Window;", "getAttributes", "()Landroid/view/WindowManager$LayoutParams;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.WindowAttributesObject;
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
		// ViewGroup.removeAllViews: guest-side child mutation is not modeled; the
		// runtime scene owns the tree, so the call is a documented no-op.
		builder.Register(Api("Landroid/view/ViewGroup;", "removeAllViews", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// View.setTag(int, Object): view tags are not consumed by the runtime UI
		// model, so the call is a documented no-op (getTag is likewise unbound).
		builder.Register(Api("Landroid/view/View;", "setTag", "(ILjava/lang/Object;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// Window insets are not modeled by the runtime UI; accepting the listener
		// registration keeps appcompat's insets plumbing moving.
		builder.Register(Api("Landroid/view/View;", "setOnApplyWindowInsetsListener", "(Landroid/view/View$OnApplyWindowInsetsListener;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// ContentFrameLayout.OnAttachListener: appcompat registers a content-view
		// attach listener; the runtime installs content directly, no-op.
		builder.Register(Api("Landroidx/appcompat/widget/ContentFrameLayout;", "setAttachListener", "(Landroidx/appcompat/widget/ContentFrameLayout$OnAttachListener;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// View.isLaidOut(): views inflated on the execution lane are laid out
		// (real semantics: true once measured/layout pass ran).
		builder.Register(Api("Landroid/view/View;", "isLaidOut", "()Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 1;
		});
		// View.postOnAnimation(Runnable): runs on the next frame; here the main
		// Looper queue (async like real Android's choreographer-posted runnable).
		builder.Register(Api("Landroid/view/View;", "postOnAnimation", "(Ljava/lang/Runnable;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			Bindings.AndroidOsHandlerBindings.PostPublic(state, RequireDex(args[1]));
			return null!;
		});
		// android.graphics.Rect: the guest ContentFrameLayout.setDecorPadding
		// mutates its mTempRect; Rect fields are not consumed further, so the
		// constructor and mutator are accepted no-ops.
		builder.Register(Api("Landroid/graphics/Rect;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/graphics/Rect;", "set", "(IIII)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// requestLayout: the runtime's scene re-measures on its own schedule; a
		// guest request is a documented no-op.
		builder.Register(Api("Landroid/view/View;", "requestLayout", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroidx/appcompat/widget/ContentFrameLayout;", "requestLayout", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// View padding getters: padding is not modeled by the runtime layout
		// engine, so reads answer 0 (honest neutral, avoids layout skew).
		builder.Register(Api("Landroid/view/View;", "getPaddingLeft", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		builder.Register(Api("Landroid/view/View;", "getPaddingTop", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		builder.Register(Api("Landroid/view/View;", "getPaddingRight", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		builder.Register(Api("Landroid/view/View;", "getPaddingBottom", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
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

	private static void RegisterResources(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		// Resources.getString(id): resolves through the APK resource table; a
		// non-string resource is a contract violation and fails closed.
		builder.Register(Api("Landroid/content/res/Resources;", "getString", "(I)Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			AndroidResourceValue value = ResolveResource(state, RequireInt(args[1]));
			return value.Kind == AndroidResourceValueKind.String
				? value.AsString()
				: throw new InvalidDataException($"Resources.getString(0x{RequireInt(args[1]):x8}) resolved to {value.Kind}, not a string.");
		});
		// Resources.getColor(id): ARGB uint from the resource table, returned as
		// the Java int representation.
		builder.Register(Api("Landroid/content/res/Resources;", "getColor", "(I)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			AndroidResourceValue value = ResolveResource(state, RequireInt(args[1]));
			return value.Kind == AndroidResourceValueKind.Color
				? unchecked((int)value.AsColor())
				: throw new InvalidDataException($"Resources.getColor(0x{RequireInt(args[1]):x8}) resolved to {value.Kind}, not a color.");
		});
		// Resources.getIdentifier(name, type, package): resolves by type/name,
		// returns 0 when absent (real Android behavior, not an exception).
		builder.Register(Api("Landroid/content/res/Resources;", "getIdentifier", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			string type = RequireString(args[2]);
			string name = RequireString(args[1]);
			if (state.Resources is null) return 0;
			try { return unchecked((int)state.Resources.GetIdentifier(type, name)); }
			catch (KeyNotFoundException) { return 0; }
		});
		// Resources.getConfiguration(): a fresh Configuration facade per call with
		// honest neutral values for the host (portrait, density 2.0x/320dpi,
		// fontScale 1.0, no dark mode, en-US locale). Guest reads of the public
		// Configuration fields resolve through iget; keys use the same
		// "Class->Name:Type" form the interpreter's FieldKey builds.
		builder.Register(Api("Landroid/content/res/Resources;", "getConfiguration", "()Landroid/content/res/Configuration;"), delegate(AndroidApiInvocation _, object[] args)
		{
			var configuration = new DexObject("Landroid/content/res/Configuration;");
			SetConfigField(configuration, "screenWidthDp", 360);
			SetConfigField(configuration, "screenHeightDp", 640);
			SetConfigField(configuration, "smallestScreenWidthDp", 360);
			SetConfigField(configuration, "densityDpi", 320);
			SetConfigField(configuration, "orientation", 1);
			SetConfigField(configuration, "screenLayout", 0x20);
			SetConfigField(configuration, "uiMode", 0x11);
			SetConfigField(configuration, "fontScale", 1.0f);
			SetConfigField(configuration, "mcc", 0);
			SetConfigField(configuration, "mnc", 0);
			var locale = new DexObject("Ljava/util/Locale;");
			SetConfigField(configuration, "locale", locale);
			SetConfigField(configuration, "locales", locale);
			// The keypad/keyboard/hardKeyboardHidden/touchscreen/theme fields default
			// to 0 via iget's DefaultFieldValue when never set; that is honest
			// (host has no hardware keyboard/touchscreen distinction).
			return configuration;
		});
	}

	private static void SetConfigField(DexObject configuration, string name, object value)
	{
		configuration.InstanceFields["Landroid/content/res/Configuration;->" + name + ":" + (value switch
		{
			int => "I",
			float => "F",
			_ => "Ljava/lang/Object;"
		})] = value;
	}

	private static AndroidResourceValue ResolveResource(AndroidFrameworkState state, int id)
	{
		if (state.Resources is null) throw new InvalidDataException("APK resource/UI session is unavailable.");
		return state.Resources.Resolve(unchecked((uint)id));
	}

	private static void RegisterTypedArray(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		// Context.obtainStyledAttributes: appcompat calls this during theme setup;
		// the runtime's native inflater does not consume styled attributes, so a
		// stable empty TypedArray facade lets the dex continue (getIndexCount == 0
		// means no attribute loop runs).
		builder.Register(Api("Landroid/content/Context;", "obtainStyledAttributes", "(Landroid/util/AttributeSet;[I)Landroid/content/res/TypedArray;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.TypedArrayObject;
		});
		builder.Register(Api("Landroid/content/Context;", "obtainStyledAttributes", "(I[I)Landroid/content/res/TypedArray;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.TypedArrayObject;
		});
		builder.Register(Api("Landroid/content/Context;", "obtainStyledAttributes", "([I)Landroid/content/res/TypedArray;"), delegate(AndroidApiInvocation invocation, object[] args)
		{
			RequireMainLane(invocation);
			return state.TypedArrayObject;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getIndexCount", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			// One pseudo-attribute: appcompat's createSubDecor requires a valid
			// AppCompat theme and iterates typed attributes; a single index lets
			// the loop run without inventing values.
			return 1;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "recycle", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		// hasValue: an app hosting AppCompatActivity must declare a Theme.AppCompat
		// descendant, so the appcompat theme probe legitimately reports values.
		builder.Register(Api("Landroid/content/res/TypedArray;", "hasValue", "(I)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 1;
		});
		// Defensive reads on an empty TypedArray return the default argument
		// (index -1 / not present), which is what a zero-length array produces.
		builder.Register(Api("Landroid/content/res/TypedArray;", "getString", "(I)Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getText", "(I)Ljava/lang/CharSequence;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getColor", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getColorStateList", "(I)Landroid/content/res/ColorStateList;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getDimension", "(IF)F"), delegate(AndroidApiInvocation _, object[] args)
		{
			return (float)RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getInt", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getResourceId", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getBoolean", "(IZ)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			// A hosted AppCompatActivity declares Theme.AppCompat, whose window
			// flags (windowActionBar etc.) are true; appcompat rejects a theme
			// where every flag is false. Report true for the theme feature probe.
			return 1;
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getFloat", "(IF)F"), delegate(AndroidApiInvocation _, object[] args)
		{
			return (float)RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getDimensionPixelSize", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getDimensionPixelOffset", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireInt(args[2]);
		});
		builder.Register(Api("Landroid/content/res/TypedArray;", "getIndex", "(I)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		// getValue: the facade TypedArray carries no typed values, so reads are
		// false (nothing stored) and the out-param is left untouched.
		builder.Register(Api("Landroid/content/res/TypedArray;", "getValue", "(ILandroid/util/TypedValue;)Z"), delegate(AndroidApiInvocation _, object[] args)
		{
			return 0;
		});
		// android.util.TypedValue: constructed by ContentFrameLayout to read theme
		// min sizes; the runtime does not consume typed values further.
		builder.Register(Api("Landroid/util/TypedValue;", "<init>", "()V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroidx/appcompat/widget/ContentFrameLayout;", "getMinWidthMajor", "()Landroid/util/TypedValue;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return new DexObject("Landroid/util/TypedValue;");
		});
		builder.Register(Api("Landroidx/appcompat/widget/ContentFrameLayout;", "getMinWidthMinor", "()Landroid/util/TypedValue;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return new DexObject("Landroid/util/TypedValue;");
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

	private static void RegisterThreadLocalRandom(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
	{
		// ThreadLocalRandom.current(): static singleton facade; draws are honest
		// pseudo-random values from a shared lock-protected CLR Random.
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "current", "()Ljava/util/concurrent/ThreadLocalRandom;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return state.ThreadLocalRandomObject;
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextInt", "()I"), delegate(AndroidApiInvocation _, object[] args)
		{
			lock (state.ThreadLocalRandomSource) return state.ThreadLocalRandomSource.Next();
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextInt", "(I)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			int bound = RequireInt(args[1]);
			if (bound <= 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "bound must be positive"));
			lock (state.ThreadLocalRandomSource) return state.ThreadLocalRandomSource.Next(bound);
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextInt", "(II)I"), delegate(AndroidApiInvocation _, object[] args)
		{
			int origin = RequireInt(args[1]);
			int bound = RequireInt(args[2]);
			if (origin >= bound) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "origin must be less than bound"));
			lock (state.ThreadLocalRandomSource) return state.ThreadLocalRandomSource.Next(origin, bound);
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextLong", "()J"), delegate(AndroidApiInvocation _, object[] args)
		{
			lock (state.ThreadLocalRandomSource) return NextLong(state.ThreadLocalRandomSource);
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextLong", "(J)J"), delegate(AndroidApiInvocation _, object[] args)
		{
			long bound = RequireLong(args[1]);
			if (bound <= 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "bound must be positive"));
			lock (state.ThreadLocalRandomSource) return NextLong(state.ThreadLocalRandomSource, bound);
		});
		builder.Register(Api("Ljava/util/concurrent/ThreadLocalRandom;", "nextDouble", "()D"), delegate(AndroidApiInvocation _, object[] args)
		{
			lock (state.ThreadLocalRandomSource) return state.ThreadLocalRandomSource.NextDouble();
		});
	}

	private static long NextLong(Random random)
	{
		long high = (long)random.Next() << 32;
		long low = (uint)random.Next();
		return high | low;
	}

	private static long NextLong(Random random, long bound)
	{
		if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
		return (long)(random.NextDouble() * bound);
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
		// getParcelable: the runtime bundle model stores only primitives/strings, so
		// a Parcelable read honestly returns null (same as an absent key).
		builder.Register(Api("Landroid/os/Bundle;", "getParcelable", "(Ljava/lang/String;)Landroid/os/Parcelable;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
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
		// Intent(Context, Class) — the explicit-component constructor. The component
		// target is retained so getComponent/setClass-style flows behave; the runtime
		// does not navigate guest activities (single-activity model), so the Class
		// arg is recorded but only the intent itself is materialized.
		builder.Register(Api("Landroid/content/Intent;", "<init>", "(Landroid/content/Context;Ljava/lang/Class;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			state.Intents.Add(Receiver(args), new IntentPeer());
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
		// ComponentName(Context, Class): constructed by the app; no state is needed
		// (only equality/toString surface is used by typical flows).
		builder.Register(Api("Landroid/content/ComponentName;", "<init>", "(Landroid/content/Context;Ljava/lang/Class;)V"), delegate(AndroidApiInvocation _, object[] args)
		{
			return null!;
		});
		builder.Register(Api("Landroid/content/ComponentName;", "getPackageName", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireDex(args[0]).InstanceFields.TryGetValue("_packageName", out var value) && value is string text ? text : state.PackageName;
		});
		builder.Register(Api("Landroid/content/ComponentName;", "getClassName", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			return RequireDex(args[0]).InstanceFields.TryGetValue("_className", out var value) && value is string text ? text : string.Empty;
		});
		builder.Register(Api("Landroid/content/ComponentName;", "toString", "()Ljava/lang/String;"), delegate(AndroidApiInvocation _, object[] args)
		{
			var component = RequireDex(args[0]);
			string packageName = component.InstanceFields.TryGetValue("_packageName", out var pkg) && pkg is string pt ? pt : state.PackageName;
			string className = component.InstanceFields.TryGetValue("_className", out var cls) && cls is string ct ? ct : string.Empty;
			return packageName + "/" + className;
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

	internal static string AsText(AndroidFrameworkState state, object value)
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

	internal static string RequireString(object value, bool allowNull = false)
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

	internal static int RequireInt(object value)
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

	internal static long RequireLong(object value)
	{
		if (value is long)
		{
			return (long)value;
		}
		throw new ArgumentException("Expected long value.");
	}

	internal static int JavaHash(string value)
	{
		int hash = 0;
		foreach (char c in value)
		{
			hash = hash * 31 + c;
		}
		return hash;
	}

	internal static bool JavaEqualsIgnoreCase(string left, string right)
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

	internal static int ReadJavaCodePoint(string value, ref int index)
	{
		char first = value[index++];
		if (char.IsHighSurrogate(first) && index < value.Length && char.IsLowSurrogate(value[index]))
		{
			return char.ConvertToUtf32(first, value[index++]);
		}
		return first;
	}

	internal static int JavaUpper(int value)
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

	internal static int JavaLower(int value)
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

	internal static string JavaTrim(string value)
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

	internal static int JavaIndexOf(string value, string search, int from)
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
