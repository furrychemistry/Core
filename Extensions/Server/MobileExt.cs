﻿#region Header
//   Vorspire    _,-'/-'/  MobileExt.cs
//   .      __,-; ,'( '/
//    \.    `-.__`-._`:_,-._       _ , . ``
//     `:-._,------' ` _,`--` -: `_ , ` ,' :
//        `---..__,,--'  (C) 2016  ` -'. -'
//        #  Vita-Nex [http://core.vita-nex.com]  #
//  {o)xxx|===============-   #   -===============|xxx(o}
//        #        The MIT License (MIT)          #
#endregion

#region References
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

using Server.Accounting;
using Server.Items;
using Server.Mobiles;
using Server.Targeting;

using VitaNex;
using VitaNex.Notify;
using VitaNex.Targets;
#endregion

namespace Server
{
	public static class MobileExtUtility
	{
		public static TimeSpan CombatHeatDelay = TimeSpan.FromSeconds(5.0);

		public static bool InCombat(this Mobile m)
		{
			return InCombat(m, CombatHeatDelay);
		}

		public static bool InCombat(this Mobile m, TimeSpan heat)
		{
			if (m == null || m.Deleted || !m.Alive)
			{
				return false;
			}

			if (m.Combatant != null && !m.Combatant.Deleted)
			{
				return true;
			}

			if (heat <= TimeSpan.Zero)
			{
				return false;
			}

			var now = DateTime.Now;
			var utc = DateTime.UtcNow;

			if (
				m.Aggressors.Any(info => info.LastCombatTime + heat >= (info.LastCombatTime.Kind == DateTimeKind.Utc ? utc : now)) ||
				m.Aggressed.Any(info => info.LastCombatTime + heat >= (info.LastCombatTime.Kind == DateTimeKind.Utc ? utc : now)))
			{
				return true;
			}

			var d = m.FindMostRecentDamageEntry(false);

			return d != null && (d.LastDamage + heat >= (d.LastDamage.Kind == DateTimeKind.Utc ? utc : now));
		}

		public static bool IsControlledBy(this Mobile m, Mobile master)
		{
			return IsControlledBy<Mobile>(m, master);
		}

		public static bool IsControlledBy<TMobile>(this Mobile m, TMobile master) where TMobile : Mobile
		{
			TMobile val;
			return IsControlled(m, out val) && val == master;
		}

		public static bool IsControlled(this Mobile m)
		{
			return IsControlled<Mobile>(m);
		}

		public static bool IsControlled(this Mobile m, out Mobile master)
		{
			return IsControlled<Mobile>(m, out master);
		}

		public static bool IsControlled<TMobile>(this Mobile m) where TMobile : Mobile
		{
			TMobile val;
			return IsControlled(m, out val);
		}

		public static bool IsControlled<TMobile>(this Mobile m, out TMobile master) where TMobile : Mobile
		{
			if (m is BaseCreature)
			{
				var c = (BaseCreature)m;
				master = c.GetMaster() as TMobile;
				return master != null && (c.Controlled || c.Summoned);
			}

			master = null;
			return false;
		}

		public static void TryParalyze(this Mobile m, TimeSpan duration, TimerStateCallback<Mobile> callback = null)
		{
			m.Paralyze(duration);

			if (callback != null)
			{
				Timer.DelayCall(duration, callback, m);
			}
		}

		public static void TryFreeze(this Mobile m, TimeSpan duration, TimerStateCallback<Mobile> callback = null)
		{
			m.Freeze(duration);

			if (callback != null)
			{
				Timer.DelayCall(duration, callback, m);
			}
		}

		private static readonly MethodInfo _SleepImpl = typeof(Mobile).GetMethod("Sleep") ??
														typeof(Mobile).GetMethod("DoSleep");

		public static void TrySleep(this Mobile m, TimeSpan duration, TimerStateCallback<Mobile> callback = null)
		{
			if (_SleepImpl != null)
			{
				VitaNexCore.TryCatch(
					() =>
					{
						_SleepImpl.Invoke(m, new object[] {duration});

						if (callback != null)
						{
							Timer.DelayCall(duration, callback, m);
						}
					});
			}
		}

		public static void SendNotification(
			this Mobile m,
			string html,
			bool autoClose = true,
			double delay = 1.0,
			double pause = 3.0,
			Color? color = null,
			Action<NotifyGump> beforeSend = null,
			Action<NotifyGump> afterSend = null)
		{
			if (m is PlayerMobile)
			{
				Notify.Send((PlayerMobile)m, html, autoClose, delay, pause, color, beforeSend, afterSend);
			}
		}

		public static void SendNotification<TGump>(
			this Mobile m,
			string html,
			bool autoClose = true,
			double delay = 1.0,
			double pause = 3.0,
			Color? color = null,
			Action<TGump> beforeSend = null,
			Action<TGump> afterSend = null) where TGump : NotifyGump
		{
			if (m is PlayerMobile)
			{
				Notify.Send((PlayerMobile)m, html, autoClose, delay, pause, color, beforeSend, afterSend);
			}
		}

		public static int GetNotorietyHue(this Mobile source, Mobile target = null)
		{
			return ComputeNotoriety(source, target).GetHue();
		}

		public static Color GetNotorietyColor(this Mobile source, Mobile target = null)
		{
			return ComputeNotoriety(source, target).GetColor();
		}

		public static NotorietyType ComputeNotoriety(this Mobile source, Mobile target = null)
		{
			if (source == null && target != null)
			{
				source = target;
			}

			if (source != null)
			{
				return (NotorietyType)Notoriety.Compute(source, target ?? source);
			}

			return NotorietyType.None;
		}

		public static void Control(this BaseCreature pet, Mobile master)
		{
			if (pet == null || pet.Deleted || pet.IsStabled || master == null || master.Deleted)
			{
				return;
			}

			pet.CurrentWayPoint = null;

			pet.ControlMaster = master;
			pet.Controlled = true;
			pet.ControlTarget = null;
			pet.ControlOrder = OrderType.Come;
			pet.Guild = null;

			pet.Delta(MobileDelta.Noto);
		}

		public static bool Stable(this BaseCreature pet, bool maxLoyalty = true, bool autoStable = true)
		{
			if (pet == null || pet.Deleted || pet.IsStabled || !(pet.ControlMaster is PlayerMobile))
			{
				return false;
			}

			var master = (PlayerMobile)pet.ControlMaster;

			pet.ControlTarget = null;
			pet.ControlOrder = OrderType.None;
			pet.Internalize();

			pet.SetControlMaster(null);
			pet.SummonMaster = null;

			pet.IsStabled = true;

			if (maxLoyalty)
			{
				pet.Loyalty = BaseCreature.MaxLoyalty; // Wonderfully happy
			}

			master.Stabled.Add(pet);

			if (autoStable)
			{
				master.AutoStabled.Add(pet);
			}

			return true;
		}

		/// <summary>
		///     Begin targeting for the specified Mobile with definded handlers
		/// </summary>
		/// <param name="m">Mobile owner of the new GenericSelectTarget instance</param>
		/// <param name="success">Success callback</param>
		/// <param name="fail">Failure callback</param>
		/// <param name="range">Maximum distance allowed</param>
		/// <param name="allowGround">Allow ground as valid target</param>
		/// <param name="flags">Target flags determine the target action</param>
		public static GenericSelectTarget<TObj> BeginTarget<TObj>(
			this Mobile m,
			Action<Mobile, TObj> success,
			Action<Mobile> fail,
			int range = -1,
			bool allowGround = false,
			TargetFlags flags = TargetFlags.None)
		{
			if (m == null || m.Deleted)
			{
				return null;
			}

			var t = new GenericSelectTarget<TObj>(success, fail, range, allowGround, flags);

			m.Target = t;

			return t;
		}

		public static TMobile GetLastKiller<TMobile>(this Mobile m, bool petMaster = true) where TMobile : Mobile
		{
			if (m == null || m.LastKiller == null)
			{
				return null;
			}

			var killer = m.LastKiller as TMobile;

			if (killer == null && petMaster && m.LastKiller is BaseCreature)
			{
				killer = ((BaseCreature)m.LastKiller).GetMaster<TMobile>();
			}

			return killer;
		}

		public static void GetEquipment(this Mobile m, out Item[] equip, out int slots)
		{
			equip = GetEquipment(m);
			slots = GetEquipmentSlots(equip);
		}

		public static Item[] GetEquipment(this Mobile m)
		{
			return FindEquipment(m).ToArray();
		}

		public static IEnumerable<Item> FindEquipment(this Mobile m)
		{
			return m == null ? new Item[0] : m.Items.Where(i => i.Layer.IsEquip());
		}

		public static int GetEquipmentSlotsMax(this Mobile m)
		{
			var max = LayerExtUtility.EquipLayers.Length - 2;
			// -2 for InnerLegs and OuterLegs, because nothing uses the layers yet...

			if (m == null)
			{
				return max;
			}

			foreach (var i in FindEquipment(m))
			{
				if (i.Layer == Layer.InnerLegs || i.Layer == Layer.OuterLegs)
				{
					// If they have an item with InnerLegs or OuterLegs, increase max slots by 1.
					++max;
				}
				else if (i.Layer == Layer.TwoHanded && i is IWeapon)
				{
					// Offset max by -1 if they have a TwoHanded weapon (which takes up the shield slot)
					--max;
				}
				else if (i.Layer == Layer.Mount && m.Race == Race.Gargoyle)
				{
					// Gargoyles can't mount...
					--max;
				}
			}

			return max;
		}

		public static int GetEquipmentSlots(this Mobile m)
		{
			return m == null ? 0 : GetEquipmentSlots(GetEquipment(m));
		}

		private static int GetEquipmentSlots(params Item[] items)
		{
			return items == null || items.Length == 0 ? 0 : items.Sum(i => i.Layer == Layer.TwoHanded && i is IWeapon ? 2 : 1);
		}

		public static TItem FindItemOnLayer<TItem>(this Mobile m, Layer layer) where TItem : Item
		{
			return m == null ? null : m.FindItemOnLayer(layer) as TItem;
		}

		public static bool HasItem<TItem>(
			this Mobile m,
			int amount = 1,
			bool children = true,
			Func<TItem, bool> predicate = null) where TItem : Item
		{
			return predicate == null
				? HasItem(m, typeof(TItem), amount, children)
				: HasItem(m, typeof(TItem), amount, children, i => predicate(i as TItem));
		}

		public static bool HasItem(
			this Mobile m,
			Type type,
			int amount = 1,
			bool children = true,
			Func<Item, bool> predicate = null)
		{
			if (m == null || type == null || amount < 1)
			{
				return false;
			}

			long total = 0;

			total =
				m.Items.Where(i => i != null && !i.Deleted && i.TypeEquals(type, children) && (predicate == null || predicate(i)))
				 .Aggregate(total, (c, i) => c + (long)i.Amount);

			if (m.Backpack != null)
			{
				total =
					m.Backpack.FindItemsByType(type, true)
					 .Where(i => i != null && !i.Deleted && i.TypeEquals(type, children) && (predicate == null || predicate(i)))
					 .Aggregate(total, (c, i) => c + (long)i.Amount);
			}

			if (m.Player && m.BankBox != null)
			{
				total =
					m.BankBox.FindItemsByType(type, true)
					 .Where(i => i != null && !i.Deleted && i.TypeEquals(type, children) && (predicate == null || predicate(i)))
					 .Aggregate(total, (c, i) => c + (long)i.Amount);
			}

			return total >= amount;
		}

		public static bool HasItems(
			this Mobile m,
			Type[] types,
			int[] amounts = null,
			bool children = true,
			Func<Item, bool> predicate = null)
		{
			if (m == null || types == null || types.Length == 0)
			{
				return false;
			}

			if (amounts == null)
			{
				amounts = new int[0];
			}

			var count = 0;

			for (var i = 0; i < types.Length; i++)
			{
				var t = types[i];
				var amount = amounts.InBounds(i) ? amounts[i] : 1;

				if (HasItem(m, t, amount, children, predicate))
				{
					++count;
				}
			}

			return count >= types.Length;
		}

		public static TItem FindItemByType<TItem>(this Mobile m) where TItem : Item
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding is TItem)
			{
				return (TItem)m.Holding;
			}

			var item = m.Items.OfType<TItem>().FirstOrDefault();

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType<TItem>() : null) ?? (b != null ? b.FindItemByType<TItem>() : null);
			}

			return item;
		}

		public static Item FindItemByType(this Mobile m, Type t)
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding.TypeEquals(t))
			{
				return m.Holding;
			}

			var item = FindEquippedItems(m, t).FirstOrDefault();

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType(t) : null) ?? (b != null ? b.FindItemByType(t) : null);
			}

			return item;
		}

		public static TItem FindItemByType<TItem>(this Mobile m, bool recurse) where TItem : Item
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding is TItem)
			{
				return (TItem)m.Holding;
			}

			var item = m.Items.OfType<TItem>().FirstOrDefault();

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType<TItem>(recurse) : null) ??
					   (b != null ? b.FindItemByType<TItem>(recurse) : null);
			}

			return item;
		}

		public static Item FindItemByType(this Mobile m, Type t, bool recurse)
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding.TypeEquals(t))
			{
				return m.Holding;
			}

			var item = FindEquippedItems(m, t).FirstOrDefault();

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType(t, recurse) : null) ?? (b != null ? b.FindItemByType(t, recurse) : null);
			}

			return item;
		}

		public static TItem FindItemByType<TItem>(this Mobile m, bool recurse, Predicate<TItem> predicate) where TItem : Item
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding is TItem)
			{
				var h = (TItem)m.Holding;

				if (predicate(h))
				{
					return h;
				}
			}

			var item = m.Items.OfType<TItem>().FirstOrDefault(i => predicate(i));

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType(recurse, predicate) : null) ??
					   (b != null ? b.FindItemByType(recurse, predicate) : null);
			}

			return item;
		}

		public static Item FindItemByType(this Mobile m, bool recurse, Predicate<Item> predicate)
		{
			if (m == null)
			{
				return null;
			}

			if (m.Holding != null)
			{
				var h = m.Holding;

				if (predicate(h))
				{
					return h;
				}
			}

			var item = m.Items.FirstOrDefault(i => predicate(i));

			if (item == null)
			{
				var p = m.Backpack;
				var b = m.FindBankNoCreate();

				item = (p != null ? p.FindItemByType(recurse, predicate) : null) ??
					   (b != null ? b.FindItemByType(recurse, predicate) : null);
			}

			return item;
		}

		public static IEnumerable<TItem> FindItemsByType<TItem>(this Mobile m) where TItem : Item
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding is TItem)
			{
				yield return (TItem)m.Holding;
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType<TItem>();

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType<TItem>();

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			foreach (var i in FindEquippedItems<TItem>(m))
			{
				yield return i;
			}
		}

		public static IEnumerable<Item> FindItemsByType(this Mobile m, Type t)
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding.TypeEquals(t))
			{
				yield return m.Holding;
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType(t);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Clear();
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType(t);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Clear();
			}

			foreach (var i in FindEquippedItems(m, t))
			{
				yield return i;
			}
		}

		public static IEnumerable<TItem> FindItemsByType<TItem>(this Mobile m, bool recurse) where TItem : Item
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding is TItem)
			{
				yield return (TItem)m.Holding;
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType<TItem>(recurse);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType<TItem>(recurse);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			foreach (var i in FindEquippedItems<TItem>(m))
			{
				yield return i;
			}
		}

		public static IEnumerable<Item> FindItemsByType(this Mobile m, Type t, bool recurse)
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding.TypeEquals(t))
			{
				yield return m.Holding;
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType(t, recurse);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Clear();
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType(t, recurse);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Clear();
			}

			foreach (var i in FindEquippedItems(m, t))
			{
				yield return i;
			}
		}

		public static IEnumerable<TItem> FindItemsByType<TItem>(this Mobile m, bool recurse, Predicate<TItem> predicate)
			where TItem : Item
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding is TItem)
			{
				var h = (TItem)m.Holding;

				if (predicate(h))
				{
					yield return h;
				}
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType(recurse, predicate);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType(recurse, predicate);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			foreach (var i in FindEquippedItems<TItem>(m).Where(i => predicate(i)))
			{
				yield return i;
			}
		}

		public static IEnumerable<Item> FindItemsByType(this Mobile m, bool recurse, Predicate<Item> predicate)
		{
			if (m == null)
			{
				yield break;
			}

			if (m.Holding != null)
			{
				var h = m.Holding;

				if (predicate(h))
				{
					yield return h;
				}
			}

			var p = m.Backpack;

			if (p != null)
			{
				var list = p.FindItemsByType(recurse, predicate);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			var b = m.FindBankNoCreate();

			if (b != null)
			{
				var list = b.FindItemsByType(recurse, predicate);

				foreach (var i in list)
				{
					yield return i;
				}

				list.Free(true);
			}

			foreach (var i in m.Items.Where(i => predicate(i)))
			{
				yield return i;
			}
		}

		public static List<TItem> GetEquippedItems<TItem>(this Mobile m) where TItem : Item
		{
			return FindEquippedItems<TItem>(m).ToList();
		}

		public static IEnumerable<TItem> FindEquippedItems<TItem>(this Mobile m) where TItem : Item
		{
			if (m == null)
			{
				yield break;
			}

			foreach (var item in m.Items.OfType<TItem>().Where(i => i.Parent == m))
			{
				yield return item;
			}
		}

		public static List<Item> GetEquippedItems(this Mobile m, Type t)
		{
			return FindEquippedItems(m, t).ToList();
		}

		public static IEnumerable<Item> FindEquippedItems(this Mobile m, Type t)
		{
			if (m == null || !t.IsEqualOrChildOf<Item>())
			{
				yield break;
			}

			var i = m.Items.Count;

			while (--i >= 0)
			{
				if (!m.Items.InBounds(i))
				{
					continue;
				}

				var item = m.Items[i];

				if (item != null && !item.Deleted && item.TypeEquals(t) && item.Parent == m)
				{
					yield return item;
				}
			}
		}

		public static void Dismount(this Mobile m, Mobile f = null)
		{
			Dismount(m, BlockMountType.None, f);
		}

		public static void Dismount(this Mobile m, BlockMountType type, Mobile f = null)
		{
			Dismount(m, type, TimeSpan.Zero, f);
		}

		public static void Dismount(this Mobile m, BlockMountType type, TimeSpan duration, Mobile f = null)
		{
			SetMountBlock(m, type, duration, true, f);
		}

		private static readonly MethodInfo _BaseMountBlock = typeof(BaseMount).GetMethod(
			"SetMountPrevention",
			BindingFlags.Static | BindingFlags.Public);

		private static readonly MethodInfo _PlayerMountBlock = typeof(PlayerMobile).GetMethod(
			"SetMountPrevention",
			BindingFlags.Instance | BindingFlags.Public);

		public static void SetMountBlock(
			this Mobile m,
			BlockMountType type,
			TimeSpan duration,
			bool dismount,
			Mobile f = null)
		{
			if (m == null)
			{
				return;
			}

			if (dismount && m.Mounted)
			{
				m.Mount.Rider = null;
			}

			if (m is PlayerMobile && _PlayerMountBlock != null)
			{
				_PlayerMountBlock.Invoke(m, new object[] {type, duration, dismount});
				return;
			}

			if (_BaseMountBlock != null)
			{
				_BaseMountBlock.Invoke(null, new object[] {m, type, duration});
			}
		}

		public static void SetAllSkills(this Mobile m, double val)
		{
			if (m == null || m.Skills == null)
			{
				return;
			}

			val = Math.Max(0.0, val);

			foreach (var skill in SkillInfo.Table.Select(s => m.Skills[s.SkillID]))
			{
				if (skill.Cap < val)
				{
					skill.SetCap(val);
				}

				skill.SetBase(val, true, false);
			}
		}

		public static void SetAllSkills(this Mobile m, double val, double cap)
		{
			if (m == null || m.Deleted || m.Skills == null)
			{
				return;
			}

			val = Math.Max(0.0, val);
			cap = Math.Max(val, cap);

			foreach (var skill in SkillInfo.Table.Select(s => m.Skills[s.SkillID]))
			{
				skill.SetCap(cap);
				skill.SetBase(val);
			}
		}

		public static int GetGumpID(this Mobile m)
		{
			var val = -1;

			if (m.Body.IsHuman)
			{
				if (m.Race == Race.Gargoyle)
				{
					val = m.Female ? 665 : 666;
				}
				else if (m.Race == Race.Elf)
				{
					val = m.Female ? 15 : 14;
				}
				else
				{
					val = m.Female ? 13 : 12;
				}
			}

			return val;
		}

		public static bool IsYoung(this Mobile m)
		{
			return m != null &&
				   (m is PlayerMobile ? ((PlayerMobile)m).Young : m.Account is Account && ((Account)m.Account).Young);
		}

		public static bool IsOnline(this Mobile m)
		{
			return m != null && m.NetState != null && m.NetState.Socket != null && m.NetState.Running;
		}

		public static GiveFlags GiveItem(this Mobile m, Item item, GiveFlags flags = GiveFlags.All, bool message = true)
		{
			return item.GiveTo(m, flags, message);
		}

		public static bool IsPvP(this Mobile m, Mobile target)
		{
			if (m == null || target == null)
			{
				return false;
			}

			if (m.Player && !target.Player)
			{
				Mobile master;
				return target.IsControlled(out master) && master.Player;
			}

			if (!m.Player && target.Player)
			{
				Mobile master;
				return m.IsControlled(out master) && master.Player;
			}

			return m.Player && target.Player;
		}

		public static bool PlayIdleSound(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var soundID = m.GetIdleSound();

			if (soundID > -1)
			{
				m.PlaySound(soundID);
				return true;
			}

			return false;
		}

		public static bool PlayDeathSound(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var soundID = m.GetDeathSound();

			if (soundID > -1)
			{
				m.PlaySound(soundID);
				return true;
			}

			return false;
		}

		public static bool PlayAngerSound(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var soundID = m.GetAngerSound();

			if (soundID > -1)
			{
				m.PlaySound(soundID);
				return true;
			}

			return false;
		}

		public static bool PlayAttackSound(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var soundID = m.GetAttackSound();

			if (soundID > -1)
			{
				m.PlaySound(soundID);
				return true;
			}

			return false;
		}

		public static bool PlayHurtSound(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var soundID = m.GetHurtSound();

			if (soundID > -1)
			{
				m.PlaySound(soundID);
				return true;
			}

			return false;
		}

		public static bool PlayAttackAnimation(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var a = AttackAnimation.Wrestle;

			if (m.Weapon is BaseWeapon)
			{
				a = (AttackAnimation)(int)((BaseWeapon)m.Weapon).Animation;
			}

			return PlayAttackAnimation(m, a);
		}

		public static bool PlayAttackAnimation(this Mobile m, AttackAnimation a)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var info = GetAttackAnimation(m, a);

			if (info != AnimationInfo.Empty)
			{
				return info.Animate(m);
			}

			return false;
		}

		public static AnimationInfo GetAttackAnimation(this Mobile m, AttackAnimation a)
		{
			if (m == null || m.Deleted)
			{
				return AnimationInfo.Empty;
			}

			int animID;

			switch (m.Body.Type)
			{
				case BodyType.Sea:
				case BodyType.Animal:
					animID = Utility.Random(5, 2);
					break;
				case BodyType.Monster:
				{
					switch (a)
					{
						case AttackAnimation.ShootBow:
						case AttackAnimation.ShootXBow:
							return AnimationInfo.Empty;
						default:
							animID = Utility.Random(4, 3);
							break;
					}
				}
					break;
				case BodyType.Human:
				{
					if (!m.Mounted)
					{
						animID = (int)a;
						break;
					}

					switch (a)
					{
						case AttackAnimation.ShootBow:
							animID = 27;
							break;
						case AttackAnimation.ShootXBow:
							animID = 28;
							break;
						case AttackAnimation.Bash2H:
						case AttackAnimation.Pierce2H:
						case AttackAnimation.Slash2H:
							animID = 29;
							break;
						default:
							animID = 26;
							break;
					}
				}
					break;
				default:
					return AnimationInfo.Empty;
			}

			return new AnimationInfo(animID, 7);
		}

		public static bool PlayDamagedAnimation(this Mobile m)
		{
			if (m == null || m.Deleted)
			{
				return false;
			}

			var info = GetDamagedAnimation(m);

			if (info != AnimationInfo.Empty)
			{
				return info.Animate(m);
			}

			return false;
		}

		public static AnimationInfo GetDamagedAnimation(this Mobile m)
		{
			if (m == null || m.Deleted || m.Mounted)
			{
				return AnimationInfo.Empty;
			}

			int action;
			int frames;

			switch (m.Body.Type)
			{
				case BodyType.Sea:
				case BodyType.Animal:
				{
					action = 7;
					frames = 5;
					break;
				}
				case BodyType.Monster:
				{
					action = 10;
					frames = 4;
					break;
				}
				case BodyType.Human:
				{
					action = 20;
					frames = 5;
					break;
				}
				default:
					return AnimationInfo.Empty;
			}

			return new AnimationInfo(action, frames);
		}
	}
}