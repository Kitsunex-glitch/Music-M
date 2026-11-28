using MusicX.Core.Helpers;
using MusicX.Core.Models;
using MusicX.Core.Models.General;
using MusicX.Core.Models.Mix;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VkNet.Abstractions;
using VkNet.Abstractions.Core;
using VkNet.Abstractions.Utils;
using VkNet.AudioBypassService.Abstractions;
using VkNet.Enums.Filters;
using VkNet.Exception;
using VkNet.Extensions.DependencyInjection;
using VkNet.Model;
using VkNet.Utils;
using Lyrics = MusicX.Core.Models.Lyrics;


namespace MusicX.Core.Services
{
    public class VkService
    {

        public readonly IVkApiCategories vkApi;
        private readonly IVkApiInvoke apiInvoke;
        private readonly Logger logger;
        private readonly string vkApiVersion = "8.154";

        public bool IsAuth = false;
        private readonly IVkTokenStore tokenStore;
        private readonly IVkApiAuthAsync auth;
        private readonly IVkApi _api;
        private readonly ICustomSectionsService _customSectionsService;
        private readonly IDeviceIdStore _deviceIdStore;
        private readonly ITokenRefreshHandler _tokenRefreshHandler;
        private readonly IRestClient _restClient;

        public async Task<MixSettingsRoot> GetStreamMixSettings(string mixId)
        {
            try
            {
                var parameters = new VkParameters
                {

                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},

                    {"mix_id", mixId},
                };

                var model = await apiInvoke.CallAsync<MixSettingsRoot>("audio.getStreamMixSettings", parameters);

                return model;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }

        }

        public VkService(Logger logger, IVkApiCategories vkApi, IVkApiInvoke apiInvoke, IVkApiVersionManager versionManager,
                         IVkTokenStore tokenStore, IVkApiAuthAsync auth, IVkApi api, ICustomSectionsService customSectionsService, IDeviceIdStore deviceIdStore, ITokenRefreshHandler tokenRefreshHandler, IRestClient restClient)
        {
            this.vkApi = vkApi;
            this.apiInvoke = apiInvoke;
            this.tokenStore = tokenStore;
            this.auth = auth;
            _api = api;
            _customSectionsService = customSectionsService;
            _deviceIdStore = deviceIdStore;
            _tokenRefreshHandler = tokenRefreshHandler;
            _restClient = restClient;


            var ver = vkApiVersion.Split('.');
            versionManager.SetVersion(int.Parse(ver[0]), int.Parse(ver[1]));

            var log = LogManager.Setup().GetLogger("Common");

            if (logger == null) this.logger = log;
            else this.logger = logger;
        }

        public async Task<string> AuthAsync(string login, string password, Func<string> twoFactorAuth)
        {
            try
            {
                logger.Info("Invoke auth with login and password");

                await auth.AuthorizeAsync(new ApiAuthParams()
                {
                    Login = login,
                    Password = password,
                    TwoFactorAuthorization = twoFactorAuth
                });


                var user = await vkApi.Users.GetAsync(new List<long>());
                _api.UserId = user[0].Id;
                IsAuth = true;

                logger.Info($"User '{user[0].Id}' successful sign in");


                return tokenStore.Token;
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
           
        }

        public async Task SetTokenAsync(string token)
        {
            try
            {
                logger.Info("Set user token");

                await auth.AuthorizeAsync(new ApiAuthParams()
                {
                    AccessToken = token
                });

                try
                {
                    var user = await vkApi.Users.GetAsync(new List<long>());
                    _api.UserId = user[0].Id;
                    logger.Info($"User '{user[0].Id}' successful sign in");
                }
                catch (VkApiMethodInvokeException e) when (e.ErrorCode == 1117) // token has expired
                {
                    if (await _tokenRefreshHandler.RefreshTokenAsync(token) is null)
                        throw;
                }

                IsAuth = true;
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
           

        }
        private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);
        public async Task<List<Audio>> GetStreamMixAudios(
            string mixId = "common",
            int append = 0,
            int count = 50,
            ImmutableDictionary<string, ImmutableArray<string>>? options = null,
            string? promptEvents = null,
            string? refParam = null,
            string? entityId = null)
        {
            try
            {
                var parameters = new VkParameters
                {
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"mix_id", mixId},
                    {"append", append},
                    {"count", count}
                };

                // Добавляем options, если не null
                if (options != null)
                {
                    parameters.Add("options", System.Text.Json.JsonSerializer.Serialize(options, _jsonSerializerOptions));
                }

                // Добавляем prompt_events, если не null
                if (!string.IsNullOrEmpty(promptEvents))
                {
                    parameters.Add("prompt_events", promptEvents);
                }

                // Добавляем ref, если не null
                if (!string.IsNullOrEmpty(refParam))
                {
                    parameters.Add("ref", refParam);
                }

                // Добавляем entity_id, если не null
                if (!string.IsNullOrEmpty(entityId))
                {
                    parameters.Add("entity_id", entityId);
                }

                var model = await apiInvoke.CallAsync<List<Audio>>("audio.getStreamMixAudios", parameters);

                return model;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }


        public async Task<ResponseData> GetAudioCatalogAsync(string url = null)
        {
            try
            {
                logger.Info("Invoke 'catalog.getAudio' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()}
                };

                if(url != null)
                {
                    parameters.Add("url", url);
                }

                var json = await apiInvoke.InvokeAsync("catalog.getAudio", parameters);
                logger.Debug("RESULT OF 'catalog.getAudio'" + json);

                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getAudio' ");

                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
            

        }

        public async ValueTask<ResponseData> GetSectionAsync(string sectionId, string startFrom = null)
        {
            try
            {
                var model = await _customSectionsService.HandleSectionRequest(sectionId, startFrom);

                if (model != null)
                    return model;
                
                logger.Info($"Invoke 'catalog.getSection' with sectionId = '{sectionId}' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"section_id", sectionId },
                    {"need_blocks", 1 },
                };

                if (startFrom != null) parameters.Add("start_from", startFrom);


                var json = await apiInvoke.InvokeAsync("catalog.getSection", parameters);
                logger.Debug("RESULT OF 'catalog.getSection'" + json);





                model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getSection' ");
                var a = model.Process();

                return a;
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> GetBlockItemsAsync(string blockId)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getBlockItems' with blockId = '{blockId}' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"block_id", blockId },
                };


                var json = await apiInvoke.InvokeAsync("catalog.getBlockItems", parameters);
                logger.Debug("RESULT OF 'catalog.getBlockItems'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getBlockItems' ");

                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> GetAudioSearchAsync(string query = null, string context= null)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getAudioSearch' with query = '{query}' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                };

                if (query != null)
                {
                    parameters.Add("query", query);
                }
                else parameters.Add("need_blocks", "1");
                if (context != null) parameters.Add("context", context);


                var json = await apiInvoke.InvokeAsync("catalog.getAudioSearch", parameters);
                logger.Debug("RESULT OF 'catalog.getAudioSearch'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);
                logger.Info("Successful invoke 'catalog.getAudioSearch' ");

                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> GetAudioArtistAsync(string artistId)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getAudioArtist' with artistId = '{artistId}' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"artist_id", artistId}

                };


                var json = await apiInvoke.InvokeAsync("catalog.getAudioArtist", parameters);
                logger.Debug("RESULT OF 'catalog.getAudioArtist'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getAudioArtist' ");


                return model.Process();
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }




        public async Task<ResponseData> GetAudioUser(string userId)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getAudio' with artistId = '{userId}' ");

                var parameters = new VkParameters
                {
                    {"extended", "1"},
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"owner_id", userId}
                };


                var json = await apiInvoke.InvokeAsync("catalog.getAudio", parameters);
                logger.Debug("RESULT OF 'catalog.getAudio'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getAudio' ");


                return model.Process();
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }



        public async Task<ResponseData> GetAudioCuratorAsync(string curatorId, string url)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getAudioCurator' with curatorId = '{curatorId}' ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"curator_id", curatorId},
                    {"url", url}
                };


                var json = await apiInvoke.InvokeAsync("catalog.getAudioCurator", parameters);
                logger.Debug("RESULT OF 'catalog.getAudioCurator'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getAudioCurator' ");


                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> GetPlaylistAsync(int count, long albumId, string accessKey, long ownerId, int offset = 0, int needOwner = 1)
        {
            try
            {
                logger.Info($"Invoke 'execute.getPlaylist' with albumId = '{albumId}' ");

                var parameters = new VkParameters
                {
                    {"v", vkApiVersion },

                    {"lang", "ru"},
                    {"audio_count", count },
                    {"need_playlist", 1 },
                    {"owner_id", ownerId},
                    {"access_key", accessKey},
                    {"func_v", 10 },
                    {"id", albumId},
                    {"audio_offset", offset },
                    
                    {"count", "10"},
                    {"need_owner", needOwner }
                };

                var json  = await apiInvoke.InvokeAsync("execute.getPlaylist", parameters);
                logger.Debug("RESULT OF 'execute.getPlaylist'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'execute.getPlaylist' ");


                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }

        }

        public async Task<bool> AudioAddAsync(long audioId, long ownerId)
        {
            try
            {
                logger.Info($"Invoke 'audio.add' with audioId = '{audioId}' and ownerId = '{ownerId}' ");

                var parameters = new VkParameters
                {
                    {"audio_id", audioId},
                    {"owner_id", ownerId}
                };


                var json = await apiInvoke.InvokeAsync("audio.add", parameters);
                logger.Debug("RESULT OF 'audio.add'" + json);


                logger.Info("Successful invoke 'audio.add' ");

                return true;
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw ex;
            }
        }

        public async Task AudioDeleteAsync(long audioId, long ownerId)
        {
            try
            {
                logger.Info($"audio.delete' with audioId = '{audioId}' and ownerId = '{ownerId}' ");

                var parameters = new VkParameters
                {
                    
                    
                    {"audio_id", audioId},
                    {"owner_id", ownerId}
                };


                var json = await apiInvoke.InvokeAsync("audio.delete", parameters);

                logger.Debug("RESULT OF 'audio.delete'" + json);


                logger.Info("Successful invoke 'audio.delete' ");
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<User> GetCurrentUserAsync()
        {
            try
            {
                var users = await vkApi.Users.GetAsync(new List<long>(), ProfileFields.Photo200);
                var currentUser = users?.FirstOrDefault();
                return currentUser;
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
           
        }

        public async Task<User> GetUserAsync(long userId)
        {
            try
            {
                var users = await vkApi.Users.GetAsync(new List<long> { userId }, ProfileFields.Photo200);
                var currentUser = users?.FirstOrDefault();
                return currentUser;
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
           
        }

        public async Task<Owner> OwnerAsync(long ownerId)
        {
            try
            {
                if (ownerId > 0)
                {
                    var users = await vkApi.Users.GetAsync(new List<long>() { ownerId });
                    var user = users?.FirstOrDefault();
                    if (user != null)
                    {
                        var owner = new Owner()
                        {
                            Id = user.Id,
                            Name = user.LastName + " " + user.FirstName
                        };
                        return owner;
                    }
                    else return null;
                }
                else
                {
                    ownerId *= (-1);
                    var groups = await vkApi.Groups.GetByIdAsync(new List<string>() { ownerId.ToString() }, "",
                        GroupsFields.Description);
                    var group = groups?.FirstOrDefault();
                    if (group != null)
                    {
                        var owner = new Owner()
                        {
                            Id = ownerId,
                            Name = group.Name,
                        };
                        return owner;
                    }
                    else return null;
                }
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
            

        }

        public async Task<JObject> AddPlaylistAsync(long playlistId, long ownerId, string accessKey)
        {
            try
            {
                logger.Info($"audio.followPlaylist' with playlistId = '{playlistId}' and ownerId = '{ownerId}' and accessKey = {accessKey} ");

                var parameters = new VkParameters
                {
                    
                    
                    {"playlist_id", playlistId},
                    {"owner_id", ownerId},
                    
                };

                if (accessKey != null) parameters.Add("access_key", accessKey);

                var json = JObject.Parse((await apiInvoke.InvokeAsync("audio.followPlaylist", parameters)));



    
                logger.Info("Successful invoke 'audio.followPlaylist' ");
                return json;
            }
            catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
            

        }

        public async Task<bool> DeletePlaylistAsync(long playlistId, long ownerId)
        {
            try
            {
                var result = await vkApi.Audio.DeletePlaylistAsync(ownerId, playlistId);
                return result;

            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> AudioGetAsync(long? playlistId, long? ownerId, string? assessKey, long offset = 0, long count = 100)
        {
            try
            {
                logger.Info($"invoke 'audio.get' with playlistId = '{playlistId}' and ownerId = '{ownerId}' and accessKey = {assessKey} ");


                var parameters = new VkParameters
                {
                    
                    
                    {"offset", offset },
                    {"count", count }
                };

                if(playlistId != null)
                {
                    parameters.Add("playlist_id", playlistId);
                }

                if (ownerId != null)
                {
                    parameters.Add("owner_id", ownerId);
                }

                if (assessKey != null)
                {
                    parameters.Add("access_key", assessKey);
                }


                var json = await apiInvoke.InvokeAsync("audio.get", parameters);

                logger.Debug("RESULT OF 'audio.get'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'audio.get' ");

                return model;
                //vkApi.Audio.GetAsync(new VkNet.Model.RequestParams.AudioGetParams() { })
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }


        }

        public async Task<ResponseData> ReplaceBlockAsync(string replaceId)
        {
            try
            {
                logger.Info($"invoke 'catalog.replaceBlocks' with replaceId = {replaceId} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"replacement_ids", replaceId},
                };



                var json = await apiInvoke.InvokeAsync("catalog.replaceBlocks", parameters);

                logger.Debug("RESULT OF 'catalog.replaceBlocks'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.replaceBlocks' ");


                return model.Process();
            }catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task StatsTrackEvents(List<TrackEvent> obj)
        {
            try
            {
                logger.Info($"invoke 'stats.trackEvents' ");

                var stats = JsonConvert.SerializeObject(obj);
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"events", stats},
                };

                Debug.WriteLine($"SEND stats.trackEvents : {stats}");

                var json = await apiInvoke.InvokeAsync("stats.trackEvents", parameters);

                logger.Info("Successful invoke 'stats.trackEvents' ");
            }catch(Exception ex)
            {
                Debug.Fail(ex.Message, ex.StackTrace);
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task FollowCurator(long curatorId)
        {
            try
            {
                logger.Info($"invoke 'audio.followCurator' with curatorId = {curatorId} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"curator_id", curatorId},
                };


                var json = await apiInvoke.InvokeAsync("audio.followCurator", parameters);

                logger.Debug("RESULT OF 'audio.followCurator'" + json);
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task UnfollowCurator(long curatorId)
        {
            try
            {
                logger.Info($"invoke 'audio.unfollowCurator' with curatorId = {curatorId} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"curator_id", curatorId},
                };


                var json = await apiInvoke.InvokeAsync("audio.unfollowCurator", parameters);

                logger.Debug("RESULT OF 'audio.unfollowCurator'" + json);
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }

        }

        public async Task FollowArtist(string artistId, string referenceId)
        {
            try
            {
                logger.Info($"invoke 'audio.followArtist' with curatorId = {artistId} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"artist_id", artistId},
                    {"ref", referenceId},
                };


                var json = await apiInvoke.InvokeAsync("audio.followArtist", parameters);

                logger.Debug("RESULT OF 'audio.followArtist'" + json);
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task UnfollowArtist(string artistId, string referenceId)
        {
            try
            {
                logger.Info($"invoke 'audio.unfollowArtist' with curatorId = {artistId} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"artist_id", artistId},
                    {"ref", referenceId},
                };


                var json = await apiInvoke.InvokeAsync("audio.unfollowArtist", parameters);

                logger.Debug("RESULT OF 'audio.unfollowArtist'" + json);
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }

        }

        public async Task<RestrictionPopupData> AudioGetRestrictionPopup(string trackCode, string audio)
        {
            try
            {
                logger.Info($"invoke 'audio.getRestrictionPopup' with trackCode= {trackCode} and audio = {audio} ");

                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"track_code", trackCode},
                    {"audio_id", audio }
                };


                var json = await apiInvoke.InvokeAsync("audio.getRestrictionPopup", parameters);

                var model = JsonConvert.DeserializeObject<RestrictionPopupData>(json);


                logger.Debug("RESULT OF 'audio.getRestrictionPopup'" + json);

                return model;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }

        }

        public async Task<ResponseData> GetPodcastsAsync(string url = null)
        {
            try
            {
                logger.Info($"Invoke 'catalog.getPodcasts' with curatorId ");

                var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"url", url},
                };


                var json = await apiInvoke.InvokeAsync("catalog.getPodcasts", parameters);
                logger.Debug("RESULT OF 'catalog.getPodcasts'" + json);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                logger.Info("Successful invoke 'catalog.getAudioCurator' ");


                return model.Process();
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<ResponseData> SectionHomeAsync()
        {
            var code = "var catalogs = API.catalog.getAudio({\"need_blocks\": 0}).catalog.sections;return API.catalog.getSection({\"need_blocks\": 1, \"section_id\": catalogs[0].id});";

            var parameters = new VkParameters
                {
                    
                    {"extended", "1"},
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"code", code},
                };


            var json = await apiInvoke.InvokeAsync("execute", parameters);


            var model = JsonConvert.DeserializeObject<ResponseData>(json);

            logger.Debug("RESULT OF 'execute'" + json);

            return model.Process();
        }

        public async Task<ResponseData> GetRecommendationsAudio(string audio)
        {
            try
            {
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"target_audio", audio},
                };


                var json = await apiInvoke.InvokeAsync("audio.getRecommendations", parameters);


                var model = JsonConvert.DeserializeObject<ResponseData>(json);

                return model;
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
          
        }

        public async Task SetBroadcastAsync(Audio? audio)
        {
            try
            {
                if (audio is null)
                {
                    await vkApi.Audio.SetBroadcastAsync();
                     return;
                }

                await vkApi.Audio.SetBroadcastAsync(audio.OwnerId + "_" + audio.Id + "_" + audio.AccessKey);
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<List<Playlist>> GetPlaylistsAsync(long ownerId)
        {
            try
            {
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"owner_id", ownerId},
                    {"count", 100}

                };


                var json = await apiInvoke.InvokeAsync("audio.getPlaylists", parameters);


                var model = JsonConvert.DeserializeObject<OldResponseData<Playlist>>(json);

                return model.Items;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }


        public async Task AddToPlaylistAsync(Audio audio, long ownerId, long playlistId)
        {
            try
            {
                var audioId = audio.OwnerId + "_" + audio.Id;
                await vkApi.Audio.AddToPlaylistAsync(ownerId, playlistId, new List<string>() { audioId });
            }catch(Exception ex)
             {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
         }

        public async Task<long> CreatePlaylistAsync(long ownerId, string title, string description, IEnumerable<Audio> tracks)
        {
            try
            {
                var audios = tracks.Select(t => t.OwnerId + "_" + t.Id).ToList();

                var result = await vkApi.Audio.CreatePlaylistAsync(ownerId, title, description, audios);

                return result.Id;
            }catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task DeletePlayListCoveAsync(long ownerId, long id)
        {
            try
            {
                var parameters = new VkParameters
                {

                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    { "v", 5.143},
                    {"owner_id", ownerId},
                    {"playlist_id", id}
                };


                var json = await apiInvoke.InvokeAsync("audio.deletePlaylistCoverPhoto", parameters);

            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }
        public async Task SetPlaylistCoverAsync(long ownerId, long playlistId, string hash, string photo)
        {
            try
            {
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    { "v", 5.143},
                    {"owner_id", ownerId},

                    {"hash", hash},
                    {"photo", photo}
                };


                var json = await apiInvoke.InvokeAsync("audio.setPlaylistCoverPhoto", parameters);

            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<string?> GetPlaylistCoverUploadServerAsync(long ownerId, long playlistId)
        {
            try
            {
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    { "v", 5.143},
                    {"owner_id", ownerId},
                    {"playlist_id", playlistId},
                };

                var json = await apiInvoke.InvokeAsync("photos.getAudioPlaylistCoverUploadServer", parameters);
                var js = JObject.Parse(json);



                return js["upload_url"].ToString() ;
            }
            catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }   
        }

        public async Task<UploadPhotoToServer> UploadPhotoToServer(string uploadUrl, string path)
        {
            using var httpClient = new HttpClient();
            using (var stream = File.OpenRead(path))
            {
                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(streamContent, "photo", Path.GetFileName(path));

                var response = await httpClient.PostAsync(uploadUrl, content).ConfigureAwait(false);

                var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                var data = JsonConvert.DeserializeObject<UploadPhotoToServer>(result);

                return data;

            }
        }


        public async Task EditPlaylistAsync(long ownerId, int playlistId, string title, string description, List<Audio> tracks)
        {
            try
            {
                var audios = tracks.Select(t => t.OwnerId + "_" + t.Id);

                var result = await vkApi.Audio.EditPlaylistAsync(ownerId, playlistId, title, description, audios);

            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task<BoomToken> GetBoomToken()
        {
            var uuid = Guid.NewGuid();

            try
            {
                var parameters = new VkParameters
                {

                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"app_id", 6767438 },
                    {"app_id", 6767438 },
                    {"timestamp", DateTimeOffset.Now.ToUnixTimeSeconds() },
                    {"app_secret", "ppBOmwQYYOMGulmaiPyK" },
                    {"package", "com.uma.musicvk" },
                    {"uuid", uuid.ToString() },
                    {"digest_hash", "2D0D1nXbs2cX1/Q8wFkyv93NHts="}
                };

                var json = await apiInvoke.InvokeAsync("auth.getCredentialsForService", parameters);

                var result = JsonConvert.DeserializeObject<List<BoomToken>>(json);

                var model = result.FirstOrDefault();

                if(model != null)
                {
                    model.Uuid = uuid.ToString();
                }

                return model;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }

        public async Task FollowOwner(long ownerId)
        {
            try
            {
                logger.Info($"Invoke 'audio.followOwner' with ownerId ");
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"owner_id", ownerId},
                };

                var json = await apiInvoke.InvokeAsync("audio.followOwner", parameters);
                logger.Debug("RESULT OF 'audio.followOwner'" + json);
                
                logger.Info("Successful invoke 'audio.followOwner' ");
            }
            catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }   
        }
        
        public async Task UnfollowOwner(long ownerId)
        {
            try
            {
                logger.Info($"Invoke 'audio.unfollowOwner' with ownerId ");
                var parameters = new VkParameters
                {
                    
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    
                    {"owner_id", ownerId},
                };

                var json = await apiInvoke.InvokeAsync("audio.unfollowOwner", parameters);
                logger.Debug("RESULT OF 'audio.unfollowOwner'" + json);
                
                logger.Info("Successful invoke 'audio.unfollowOwner' ");
            }
            catch(Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }   
        }

        public async Task<Lyrics> GetLyrics(string audioId)
        {
            try
            {
                logger.Info($"Invoke 'audio.getLyrics' with audioId  {audioId}");
                var parameters = new VkParameters
                {

                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},

                    {"audio_id", audioId},
                };

                var json = await apiInvoke.InvokeAsync("audio.getLyrics", parameters);
                logger.Debug("RESULT OF 'audio.getLyrics'" + json);

                var model = JsonConvert.DeserializeObject<Lyrics>(json);

                logger.Info("Successful invoke 'audio.getLyrics' ");

                return model;
            }
            catch (Exception ex)
            {
                logger.Error("VK API ERROR:");
                logger.Error(ex, ex.Message);
                throw;
            }
        }
        private static readonly string[] MiniAppFields =
        {
            "has_vk_connect",
            "is_vkui_internal",
            "webview_url",
            "screen_orientation",
            "mobile_controls_type",
            "splash_screen",
            "background_loader_color",
            "placeholder_info",
            "hide_tabbar",
            "track_code",
            "author_owner_id",
            "preload_ad_types",
            "ad_config",
            "can_cache",
            "icon_75",
            "icon_139",
            "icon_150",
            "icon_576",
            "need_show_unverified_screen",
            "is_in_catalog"
        };

        public async Task<ResolveScreenNameResponse> GetMiniApp(string appId, string url)
        {
            var normalizedUrl = string.IsNullOrWhiteSpace(url) ? null : url;
            var deviceId = await _deviceIdStore.GetDeviceIdAsync();
            var numericAppId = ParseAppId(appId);

            // 1. Сначала пробуем получить подписанную ссылку для десктопного веба (platform = "web")
            if (numericAppId > 0)
            {
                try
                {
                    var embeddedParams = new VkParameters
                    {
                        {"app_id", numericAppId},
                        {"platform", "web"},
                        {"v", "5.199"}
                    };

                    var rawEmbedded = await apiInvoke.InvokeAsync("apps.getEmbeddedUrl", embeddedParams);
                    LogMiniApp($"apps.getEmbeddedUrl(web) appId={appId} raw={rawEmbedded}");

                    var parsedEmbedded = DeserializeMiniApp(rawEmbedded);
                    if (!string.IsNullOrEmpty(parsedEmbedded?.EmbeddedUri?.ViewUrl) ||
                        !string.IsNullOrEmpty(parsedEmbedded?.Object?.WebviewUrl))
                    {
                        return parsedEmbedded;
                    }
                }
                catch (Exception e)
                {
                    LogMiniApp($"apps.getEmbeddedUrl(web) appId={appId} ERROR {e.GetType().Name}: {e.Message}");
                }
            }

            // 2. Если apps.getEmbeddedUrl не вернул ссылку — пробуем apps.get с platform = "web"
            try
            {
                var parameters = new VkParameters
                {
                    {"app_id", numericAppId},
                    {"platform", "web"}, // было "android"
                    {"extended", true},
                    {"v", "5.199"}
                };

                var raw = await apiInvoke.InvokeAsync("apps.get", parameters);
                LogMiniApp($"apps.get(web) appId={appId} raw={raw}");

                var fromAppsGet = ParseAppsGet(raw, appId);
                if (fromAppsGet != null && (!string.IsNullOrEmpty(fromAppsGet.Object?.WebviewUrl) ||
                                            !string.IsNullOrEmpty(fromAppsGet.EmbeddedUri?.ViewUrl)))
                {
                    return fromAppsGet;
                }
            }
            catch (Exception e)
            {
                LogMiniApp($"apps.get(web) appId={appId} ERROR {e.GetType().Name}: {e.Message}");
            }

            // 3. Fallback на ваш текущий код с execute.resolveScreenName (если метод выше недоступен для данного токена)
            var attempts = new List<(string Name, string Method, VkParameters Parameters)>
            {
                ("execute.resolveScreenName/func_v=23",
                    "execute.resolveScreenName",
                    BuildResolveParameters(appId, normalizedUrl, "23", MiniAppFields, deviceId)),
                ("execute.resolveScreenName/func_v=14",
                    "execute.resolveScreenName",
                    BuildResolveParameters(appId, normalizedUrl, "14", MiniAppFields, deviceId)),
                ("execute.resolveScreenName/no-func_v",
                    "execute.resolveScreenName",
                    BuildResolveParameters(appId, normalizedUrl, null, MiniAppFields, deviceId)),
            };

            ResolveScreenNameResponse model = null;
            Exception lastError = null;

            foreach (var attempt in attempts)
            {
                try
                {
                    var raw = await apiInvoke.InvokeAsync(attempt.Method, attempt.Parameters);
                    LogMiniApp($"{attempt.Name} appId={appId} raw={raw}");

                    var parsed = DeserializeMiniApp(raw);
                    if (parsed != null)
                    {
                        model ??= parsed;
                        if (!string.IsNullOrEmpty(parsed.Object?.WebviewUrl) ||
                            !string.IsNullOrEmpty(parsed.EmbeddedUri?.ViewUrl))
                            return parsed;
                    }
                }
                catch (Exception e)
                {
                    lastError = e;
                    LogMiniApp($"{attempt.Name} appId={appId} ERROR {e.GetType().Name}: {e.Message}");
                    logger.Error(e);
                }
            }

            if (model == null && lastError != null)
                throw lastError;

            return model ?? new ResolveScreenNameResponse("mini_app", 0, null, null);
        }


        private VkParameters BuildResolveParameters(string appId, string url, string funcV,
            IEnumerable<string> appFields, string deviceId)
        {
            var parameters = new VkParameters
            {
                {"device_id", deviceId},
                {"screen_name", appId},
                // Явная версия API: без неё execute.resolveScreenName может
                // не возвращать webview_url.
                {"v", "5.199"}
            };

            if (funcV != null)
                parameters["func_v"] = funcV;

            if (!string.IsNullOrEmpty(url))
                parameters["url"] = url;

            if (appFields != null)
                parameters["app_fields"] = string.Join(",", appFields);

            return parameters;
        }

        private static ResolveScreenNameResponse DeserializeMiniApp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "{}")
                return null;

            try
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                    {
                        NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy()
                    }
                };

                return JsonConvert.DeserializeObject<ResolveScreenNameResponse>(raw, settings);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Разбирает ответ apps.get (массив приложений) в привычную модель,
        /// подставляя webview_url/иконки, если они там есть.
        /// </summary>
        private static ResolveScreenNameResponse ParseAppsGet(string raw, string appId)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                var root = JObject.Parse(raw);
                var items = root["response"]?["items"] as JArray ?? root["response"] as JArray;
                var item = items?.FirstOrDefault();
                if (item == null)
                    return null;

                string Str(string name) => item[name]?.Type == JTokenType.Null ? null : item[name]?.ToString();

                var id = int.TryParse(Str("id"), out var parsedId) ? parsedId : 0;

                var resolved = new ResolvedObject(
                    "mini_app",
                    id,
                    Str("title"),
                    0,
                    int.TryParse(Str("author_owner_id"), out var owner) ? owner : 0,
                    Str("track_code"),
                    Str("webview_url"),
                    0,
                    Str("icon_139"),
                    Str("icon_150"),
                    Str("icon_278"),
                    Str("icon_576"),
                    Str("icon_75"),
                    false,
                    false,
                    null,
                    0,
                    false,
                    null);

                return new ResolveScreenNameResponse("mini_app", id, resolved, null);
            }
            catch
            {
                return null;
            }
        }

        private static readonly object MiniAppLogLock = new();

        private static void LogMiniApp(string line)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MusicMMKZ", "miniapp-getapp.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                lock (MiniAppLogLock)
                {
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
                }
            }
            catch
            {
                // лог — не критично
            }
        }

        public async Task<AppLaunchParamsResponse> GetMiniAppLaunchParams(long appId)
        {
            var parameters = new VkParameters
            {
                {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                {"mini_app_id", appId},
                {"platform", "web"},
                {"referer", "recs"}
            };

            try
            {
                var model = await apiInvoke.CallAsync<AppLaunchParamsResponse>("apps.getAppLaunchParams",
                    parameters);

                return model;
            }
            catch (Exception e)
            {
                logger.Error(e);
                throw;
            }
        }

        /// <summary>
        /// Разрешает приложение через execute.resolveScreenName и возвращает «сырой» ответ VK
        /// (с webview_url, embedded_uri.view_url и access_token игры) без потери полей
        /// при десериализации в модель.
        /// </summary>
        public async Task<JObject> ResolveMiniAppRawAsync(string appId, string url)
        {
            try
            {
                var parameters = new VkParameters
                {
                    {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                    {"screen_name", appId},
                    {"func_v", 23},
                    {"v", "5.199"},
                    {"app_fields", string.Join(",", MiniAppFields)}
                };

                if (!string.IsNullOrWhiteSpace(url))
                    parameters["url"] = url;

                var raw = await apiInvoke.InvokeAsync("execute.resolveScreenName", parameters);
                LogMiniApp($"ResolveMiniAppRawAsync appId={appId} raw={raw}");

                return string.IsNullOrWhiteSpace(raw) ? null : JObject.Parse(raw);
            }
            catch (Exception e)
            {
                logger.Error(e);
                LogMiniApp($"ResolveMiniAppRawAsync appId={appId} ERROR {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Получает access_token мини-приложения. Для игр (html5) токен уже лежит
        /// в embedded_uri.view_url; для обычных мини-приложений — в webview_url.
        /// Если токена нет — пробуем apps.getAppLaunchParams, затем oauth/authorize.
        /// </summary>
        public async Task<string> GetMiniAppTokenAsync(string appId, string sourceUrl, string scope)
        {
            var raw = await ResolveMiniAppRawAsync(appId, $"https://vk.ru/{appId}");

            var candidates = new[]
            {
                raw?["embedded_uri"]?["view_url"]?.ToString(),
                raw?["embedded_uri"]?["original_url"]?.ToString(),
                raw?["object"]?["webview_url"]?.ToString(),
                sourceUrl
            };

            foreach (var candidate in candidates)
            {
                var token = TryExtractAccessToken(candidate);
                if (!string.IsNullOrEmpty(token))
                {
                    LogMiniApp($"GetMiniAppTokenAsync appId={appId} token from URL");
                    return token;
                }
            }

            // apps.getAppLaunchParams иногда содержит токен настроек приложения.
            try
            {
                var appIdNumber = ParseAppId(appId);
                var launchParams = await GetMiniAppLaunchParams(appIdNumber);

                if (!string.IsNullOrEmpty(launchParams?.VkAccessTokenSettings))
                {
                    LogMiniApp($"GetMiniAppTokenAsync appId={appId} token from launch params");
                    return launchParams.VkAccessTokenSettings;
                }
            }
            catch (Exception e)
            {
                LogMiniApp($"GetMiniAppTokenAsync launch params failed: {e.GetType().Name}: {e.Message}");
            }

            // Последняя попытка — oauth/authorize с source_url мини-приложения.
            var webviewUrl = raw?["object"]?["webview_url"]?.ToString();
            var oauthSource = !string.IsNullOrEmpty(webviewUrl)
                ? webviewUrl
                : raw?["embedded_uri"]?["view_url"]?.ToString() ?? sourceUrl;

            var response = await GetMiniAppCredentialToken(oauthSource, scope);
            LogMiniApp($"GetMiniAppTokenAsync appId={appId} token from oauth");

            return response.AccessToken;
        }

        /// <summary>Достаёт access_token из URL (query или fragment).</summary>
        private static string TryExtractAccessToken(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                var uri = new Uri(url);

                var token = ExtractParam(uri.Query, "access_token");
                if (!string.IsNullOrEmpty(token))
                    return token;

                if (!string.IsNullOrEmpty(uri.Fragment))
                    return ExtractParam("?" + uri.Fragment.TrimStart('#'), "access_token");
            }
            catch
            {
                // некорректный URL — игнорируем
            }

            return null;
        }

        /// <summary>
        /// Безопасно достаёт параметр из query-строки (без исключений на пустых значениях).
        /// </summary>
        private static string ExtractParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || query == "?")
                return null;

            var trimmed = query.StartsWith('?') ? query[1..] : query;

            foreach (var pair in trimmed.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;

                if (!pair.AsSpan(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = pair[(eq + 1)..];
                return string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
            }

            return null;
        }

        /// <summary>Преобразует «app12345» / «12345» в long.</summary>
        public static long ParseAppId(string appId)
        {
            if (string.IsNullOrEmpty(appId))
                return 0;

            var digits = appId.StartsWith("app", StringComparison.OrdinalIgnoreCase)
                ? appId[3..]
                : appId;

            return long.TryParse(digits, out var parsed) ? parsed : 0;
        }

        public async Task<CredentialResponse> GetMiniAppCredentialToken(string sourceUrl, string scope)
        {
            var parameters = new VkParameters
            {
                {"device_id", await _deviceIdStore.GetDeviceIdAsync()},
                {"display", "android"},
                {"scope", scope},
                {"response_type", "token"},
                {"redirect_uri", "https://oauth.vk.ru/blank.html"},
                {"client_id", 52384530},
                {"source_url", sourceUrl},
                {"https", true},
                {"v", vkApiVersion},
                {"access_token", _api.Token}
            };

            try
            {
                var response = await _restClient.PostAsync(new("https://api.vk.ru/oauth/authorize"), parameters, Encoding.UTF8);

                var fragment = response.ResponseUri?.Fragment;
                if (string.IsNullOrEmpty(fragment) || !fragment.StartsWith("#access_token="))
                    throw new VkApiException("Access token not found");

                var query = Url.ParseQueryString("?" + fragment[1..]);
                return new CredentialResponse(query["access_token"]);
            }
            catch (Exception e)
            {
                logger.Error(e);
                throw;
            }
        }
    }
}
