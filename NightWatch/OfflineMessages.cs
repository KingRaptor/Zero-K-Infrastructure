﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using LobbyClient;
using ZkData;

namespace NightWatch
{
	class OfflineMessages
	{
		const int MessageDelay = 66;
		readonly TasClient client;

		public OfflineMessages(TasClient client)
		{
			this.client = client;
			client.UserAdded += client_UserAdded;
			client.Said += client_Said;
			client.LoginAccepted += client_LoginAccepted;
			client.ChannelUserAdded += client_ChannelUserAdded;
		    try
		    {
		        using (var db = new ZkDataContext())
		        {
		            db.Database.ExecuteSqlCommand("DELETE FROM LobbyMessages WHERE Created < {0}", DateTime.UtcNow.AddDays(-14));
		        }
		    }
		    catch (Exception ex)
		    {
		        Trace.TraceError(ex.ToString());
		    }
		}

		void client_ChannelUserAdded(object sender, ChannelUserInfo e)
		{
			Task.Run(async () =>
				{
					try
					{
						var chan = e.Channel.Name;
						List<LobbyMessage> messages;
					    foreach (var user in e.Users) {
					        using (var db = new ZkDataContext()) {
					            messages = db.LobbyMessages.Where(x => x.TargetName == user.Name && x.Channel == chan).OrderBy(x => x.Created).ToList();
					            db.LobbyMessages.DeleteAllOnSubmit(messages);
					            db.SubmitChanges();
					        }
					        foreach (var m in messages) {
					            var text = string.Format("!pm|{0}|{1}|{2}|{3}", m.Channel, m.SourceName, m.Created.ToString(CultureInfo.InvariantCulture), m.Message);
					            await client.Say(SayPlace.User, user.Name, text, false);
					            await Task.Delay(MessageDelay);
					        }
					    }
					}
					catch (Exception ex)
					{
						Trace.TraceError("Error adding user: {0}", ex);
					}
				});
		}

		void client_LoginAccepted(object sender, TasEventArgs e)
		{
			using (var db = new ZkDataContext()) foreach (var c in db.LobbyChannelSubscriptions.Select(x => x.Channel).Distinct()) client.JoinChannel(c);
		}

		void client_Said(object sender, TasSayEventArgs e)
		{
		    User user;
		    if (!client.ExistingUsers.TryGetValue(e.UserName, out user)) return;
			if (e.Place == SayPlace.Channel && e.Channel != "main")
			{
				Task.Factory.StartNew(() =>
					{
						try
						{
							using (var db = new ZkDataContext())
							{
							    Channel channel;
							    if (!client.JoinedChannels.TryGetValue(e.Channel, out channel)) return;
								foreach (var s in db.LobbyChannelSubscriptions.Where(x => x.Channel == e.Channel).Select(x=>x.Account))
								{
									if (!channel.Users.ContainsKey(s.Name)) {
									    var fac = db.Factions.FirstOrDefault(x => x.Shortcut == e.Channel);
                                        // if faction channel check if allowed
                                        if (fac == null || (fac.FactionID == s.AccountID && s.Level >= GlobalConst.FactionChannelMinLevel)) {

                                            var message = new LobbyMessage()
                                                          {
                                                              SourceLobbyID = user.AccountID,
                                                              SourceName = e.UserName,
                                                              Created = DateTime.UtcNow,
                                                              Message = e.Text,
                                                              TargetName = s.Name,
                                                              Channel = e.Channel
                                                          };
                                            db.LobbyMessages.InsertOnSubmit(message);
                                        }
									}
								}
								db.SubmitChanges();
							}
						}
						catch (Exception ex)
						{
							Trace.TraceError("Error storing message: {0}", ex);
						}
					});
			}
			else if (e.Place == SayPlace.User)
			{
				Task.Factory.StartNew(() =>
					{
						try
						{
							if (e.Place == SayPlace.User)
							{
								if (e.Text.StartsWith("!pm"))
								{
									var regex = Regex.Match(e.Text, "!pm ([^ ]+) (.+)");
									if (regex.Success)
									{
										var name = regex.Groups[1].Value;
										var text = regex.Groups[2].Value;

										var message = new LobbyMessage()
										              { SourceLobbyID = user.AccountID, SourceName = e.UserName, Created = DateTime.UtcNow, Message = text, TargetName = name };
										using (var db = new ZkDataContext())
										{
											db.LobbyMessages.InsertOnSubmit(message);
											db.SubmitChanges();
										}
									}
								}
								else if (e.Text.StartsWith("!subscribe"))
								{
									var regex = Regex.Match(e.Text, "!subscribe #([^ ]+)");
									if (regex.Success)
									{
										var chan = regex.Groups[1].Value;
										if (chan != "main")
										{
											using (var db = new ZkDataContext()) {
												Faction channelFaction = db.Factions.FirstOrDefault(x=> chan == x.Name);
												//Clan channelClan = db.Clans.FirstOrDefault(x=> x.GetClanChannel() == chan);
												Account account = Account.AccountByName(db, e.UserName);
												if (!(account.IsZeroKAdmin) &&
													((chan == AuthService.ModeratorChannel)	
                                                    || (channelFaction != null && account.Faction != channelFaction)
													//|| (channelClan != null && account.Clan != channelClan)
                                                    ))
                                                {
                                                    client.Say(SayPlace.User, user.Name, "Not authorized to subscribe to this channel", false);
                                                }
                                                else
                                                {
                                                    var accountID = account.AccountID;
                                                    var subs = db.LobbyChannelSubscriptions.FirstOrDefault(x => x.AccountID == accountID && x.Channel == chan);
                                                    if (subs == null)
                                                    {
                                                        subs = new LobbyChannelSubscription() { AccountID = accountID, Channel = chan };
                                                        db.LobbyChannelSubscriptions.InsertOnSubmit(subs);
                                                        db.SubmitChanges();
                                                        client.JoinChannel(chan);
                                                    }
                                                    client.Say(SayPlace.User, user.Name, "Subscribed", false);
                                                }
											}
										}
									}
								}
								else if (e.Text.StartsWith("!unsubscribe"))
								{
									var regex = Regex.Match(e.Text, "!unsubscribe #([^ ]+)");
									if (regex.Success)
									{
										var chan = regex.Groups[1].Value;

										using (var db = new ZkDataContext()) {
										    var accountID = Account.AccountByName(db, e.UserName).AccountID;
											var subs = db.LobbyChannelSubscriptions.FirstOrDefault(x => x.AccountID == accountID && x.Channel == chan);
											if (subs != null)
											{
												db.LobbyChannelSubscriptions.DeleteOnSubmit(subs);
												db.SubmitChanges();
											}
											client.Say(SayPlace.User, user.Name, "Unsubscribed", false);
										}
									}
								}
                                else if (e.Text.Equals("!listsubscriptions"))
                                {
                                    using (var db = new ZkDataContext())
                                    {
                                        var accountID = Account.AccountByName(db, e.UserName).AccountID;
                                        var subscriptionList = "No channels subscribed.";
                                        var subs = db.LobbyChannelSubscriptions.Where(x => x.AccountID == accountID).OrderBy(x => x.Channel).Select(x => x.Channel );
                                        if (subs != null)
                                        {
                                            subscriptionList = "Subscribed to: " + String.Join(", ", subs);
                                        }
                                        client.Say(SayPlace.User, user.Name, subscriptionList, false);
                                    }
                                }

							}
						}
						catch (Exception ex)
						{
							Trace.TraceError("Error sending stored message: {0}", ex);
						}
					});
			}
		}


		void client_UserAdded(object sender, User user)
		{
			Task.Factory.StartNew(() =>
				{
					try
					{
						List<LobbyMessage> messages;
						using (var db = new ZkDataContext())
						
						{
							messages = db.LobbyMessages.Where(x => (x.TargetLobbyID == user.AccountID || x.TargetName == user.Name) && x.Channel == null).OrderBy(x => x.Created).ToList();
							db.LobbyMessages.DeleteAllOnSubmit(messages);
							db.SubmitChanges();
						}
						foreach (var m in messages)
						{
							var text = string.Format("!pm|{0}|{1}|{2}|{3}", m.Channel, m.SourceName, m.Created.ToString(CultureInfo.InvariantCulture), m.Message);
							client.Say(SayPlace.User, user.Name, text, false);
							Thread.Sleep(MessageDelay);
						}
					}

					catch (Exception ex)
					{
						Trace.TraceError("Error sending PM:{0}", ex);
					}
				});
		}
	}
}
