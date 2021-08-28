using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using DynamicGameAssets.Game;
using DynamicGameAssets.PackData;
using DynamicGameAssets.Patches;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using Newtonsoft.Json;
using SpaceCore;
using SpaceCore.Events;
using SpaceShared;
using SpaceShared.APIs;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;
using System.Runtime.CompilerServices;

// TODO: Shirts don't work properly if JA is installed? (Might look funny, might make you run out of GPU memory thanks to SpaceCore tilesheet extensions)
// TODO: Cooking recipes show when crafting, but not in the collection.

// TODO: Converter & Migration
// TODO: Objects: Donatable to museum?
// TODO: Objects (or general): Deconstructor output patch?
// TODO: Objects: Fish tank display?
// TODO: Objects: Stuff on tables, while eating?
// TODO: Objects: Preserve overrides?
// TODO: Objects: warp totems?
// TODO: Crops: Can grow in IndoorPot field (like ancient seeds)
// TODO: Crops: Can grow in greenhouse?
// TODO: Crops: getRandomWildCropForSeason support?
// TODO: General validation, optimization (cache Data in IDGAItem's), not crashing when an item is missing, etc.
// TODO: Look into Gourmand requests?
/* TODO:
 * ? bundles
 * ? quests
 * fishing
 * ? walls/floors
 * Custom Ore Nodes & Custom Resource Clumps (with permission from aedenthorn)
 * ? paths
 * ? buildings (I have a working unreleased framework for this already)
 * NOT farm animals (FAVR)
 * NOT NPCs (covered by CP indirectly)
 * ????farm types????
 * NOT critters (needs AI stuff, can be its own mod)
 * NOT quests
 * NOT mail (MFM)
 * secret notes?
 * NOT trees (BURT)
 * ??? grass + grass starters?
 */
// TODO: API
// TODO: Converter (packs) and converter (items)
// Stretch: In-game editor

namespace DynamicGameAssets
{
    public class Mod : StardewModdingAPI.Mod, IAssetLoader, IAssetEditor
    {
        public static Mod instance;
        internal ContentPatcher.IContentPatcherAPI cp;
        private Harmony harmony;

        public static readonly int BaseFakeObjectId = 1720;
        public static ContentPack DummyContentPack;

        internal static Dictionary<string, ContentPack> contentPacks = new Dictionary<string, ContentPack>();

        internal static Dictionary<int, string> itemLookup = new Dictionary<int, string>();

        internal static List<DGACustomCraftingRecipe> customCraftingRecipes = new List<DGACustomCraftingRecipe>();
        internal static List<DGACustomForgeRecipe> customForgeRecipes = new List<DGACustomForgeRecipe>();
        internal static Dictionary<string, List<MachineRecipePackData>> customMachineRecipes = new Dictionary<string, List<MachineRecipePackData>>();
        internal static List<TailoringRecipePackData> customTailoringRecipes = new List<TailoringRecipePackData>();

        private static readonly PerScreen<StateData> _state = new PerScreen<StateData>( () => new StateData() );
        internal static StateData State => _state.Value;

        public static CommonPackData Find( string fullId )
        {
            int slash = fullId.IndexOf( '/' );
            string pack = fullId.Substring( 0, slash );
            string item = fullId.Substring( slash + 1 );
            return contentPacks.ContainsKey( pack ) ? contentPacks[ pack ].Find( item ) : null;
        }

        public static List<ContentPack> GetPacks()
        {
            return new List<ContentPack>( contentPacks.Values );
        }
        
        public override void Entry(IModHelper helper)
        {
            instance = this;
            Log.Monitor = Monitor;

            //nullPack.Manifest.ExtraFields.Add( "DGA.FormatVersion", -1 );
            //nullPack.Manifest.ExtraFields.Add( "DGA.ConditionsVersion", "1.0.0" );
            DummyContentPack = new ContentPack( new NullContentPack() );

            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Display.MenuChanged += OnMenuChanged;

            helper.ConsoleCommands.Add( "dga_list", "List all items.", OnListCommand );
            helper.ConsoleCommands.Add( "dga_add", "`dga_add <mod.id/ItemId> [amount] - Add an item to your inventory.", OnAddCommand, AddCommandAutoComplete );
            helper.ConsoleCommands.Add( "dga_force", "Do not use", OnForceCommand );
            helper.ConsoleCommands.Add( "dga_reload", "Reload all content packs.", OnReloadCommand/*, ReloadCommandAutoComplete*/ );
            helper.ConsoleCommands.Add( "dga_clean", "Remove all invalid items from the currently loaded save.", OnCleanCommand );

            harmony = new Harmony( ModManifest.UniqueID );
            harmony.PatchAll();
            harmony.Patch( typeof( IClickableMenu ).GetMethod( "drawHoverText", new[] { typeof( SpriteBatch ), typeof( StringBuilder ), typeof( SpriteFont ), typeof( int ), typeof( int ), typeof( int ), typeof( string ), typeof( int ), typeof( string[] ), typeof( Item ), typeof( int ), typeof( int ), typeof( int ), typeof( int ), typeof( int ), typeof( int ),typeof( CraftingRecipe ), typeof( IList<Item> ) } ), transpiler: new HarmonyMethod( typeof( DrawHoverTextPatch ).GetMethod( "Transpiler" ) ) );
            harmony.Patch( typeof( SpriteBatch ).GetMethod( "Draw", new[] { typeof( Texture2D ), typeof( Rectangle ), typeof( Rectangle? ), typeof( Color ), typeof( float ), typeof( Vector2 ), typeof( SpriteEffects ), typeof( float ) } ), prefix: new HarmonyMethod( typeof( SpriteBatchTileSheetAdjustments ).GetMethod( nameof( SpriteBatchTileSheetAdjustments.Prefix1 ) ) ) { before = new string[] { "spacechase0.SpaceCore" } } );
            harmony.Patch( typeof( SpriteBatch ).GetMethod( "Draw", new[] { typeof( Texture2D ), typeof( Rectangle ), typeof( Rectangle? ), typeof( Color ), } ), prefix: new HarmonyMethod( typeof( SpriteBatchTileSheetAdjustments ).GetMethod( nameof( SpriteBatchTileSheetAdjustments.Prefix2 ) ) ) { before = new string[] { "spacechase0.SpaceCore" } } );
            harmony.Patch( typeof( SpriteBatch ).GetMethod( "Draw", new[] { typeof( Texture2D ), typeof( Vector2 ), typeof( Rectangle? ), typeof( Color ), typeof( float ), typeof( Vector2 ), typeof( Vector2 ), typeof( SpriteEffects ), typeof( float ) } ), prefix: new HarmonyMethod( typeof( SpriteBatchTileSheetAdjustments ).GetMethod( nameof( SpriteBatchTileSheetAdjustments.Prefix3 ) ) ) { before = new string[] { "spacechase0.SpaceCore" } } );
            harmony.Patch( typeof( SpriteBatch ).GetMethod( "Draw", new[] { typeof( Texture2D ), typeof( Vector2 ), typeof( Rectangle? ), typeof( Color ), typeof( float ), typeof( Vector2 ), typeof( float ), typeof( SpriteEffects ), typeof( float ) } ), prefix: new HarmonyMethod( typeof( SpriteBatchTileSheetAdjustments ).GetMethod( nameof( SpriteBatchTileSheetAdjustments.Prefix4 ) ) ) { before = new string[] { "spacechase0.SpaceCore" } } );
            harmony.Patch( typeof( SpriteBatch ).GetMethod( "Draw", new[] { typeof( Texture2D ), typeof( Vector2 ), typeof( Rectangle? ), typeof( Color ) } ), prefix: new HarmonyMethod( typeof( SpriteBatchTileSheetAdjustments ).GetMethod( nameof( SpriteBatchTileSheetAdjustments.Prefix5 ) ) ) { before = new string[] { "spacechase0.SpaceCore" } } );
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            cp = Helper.ModRegistry.GetApi<ContentPatcher.IContentPatcherAPI>( "Pathoschild.ContentPatcher" );

            var spacecore = Helper.ModRegistry.GetApi<ISpaceCoreApi>( "spacechase0.SpaceCore" );
            spacecore.RegisterSerializerType( typeof( CustomObject ) );
            spacecore.RegisterSerializerType(typeof( Game.CustomCraftingRecipe));
            spacecore.RegisterSerializerType(typeof(CustomBasicFurniture));
            spacecore.RegisterSerializerType(typeof(CustomBedFurniture));
            spacecore.RegisterSerializerType(typeof(CustomTVFurniture));
            spacecore.RegisterSerializerType(typeof(CustomFishTankFurniture));
            spacecore.RegisterSerializerType(typeof(CustomStorageFurniture));
            spacecore.RegisterSerializerType(typeof(CustomCrop));
            spacecore.RegisterSerializerType(typeof(CustomGiantCrop));
            spacecore.RegisterSerializerType(typeof(CustomMeleeWeapon));
            spacecore.RegisterSerializerType(typeof(CustomBoots));
            spacecore.RegisterSerializerType(typeof(CustomHat));
            spacecore.RegisterSerializerType(typeof(CustomFence));
            spacecore.RegisterSerializerType(typeof(CustomBigCraftable));
            spacecore.RegisterSerializerType(typeof(CustomFruitTree));
            spacecore.RegisterSerializerType(typeof(CustomShirt));
            spacecore.RegisterSerializerType(typeof(CustomPants));

            LoadContentPacks();

            RefreshSpritebatchCache();
        }

        private ConditionalWeakTable< Farmer, Holder< string > > prevBootsFrame = new ConditionalWeakTable< Farmer, Holder< string > >();
        private void OnUpdateTicked( object sender, UpdateTickedEventArgs e )
        {
            State.AnimationFrames++;

            // Support animated boots colors
            foreach ( var farmer in Game1.getAllFarmers() )
            {
                if ( farmer.boots.Value is CustomBoots cboots )
                {
                    string frame = cboots.Data.parent.GetTextureFrame( cboots.Data.FarmerColors );
                    if ( prevBootsFrame.GetOrCreateValue( farmer ).Value != frame )
                    {
                        prevBootsFrame.AddOrUpdate( farmer, new Holder<string>(frame) );
                        farmer.FarmerRenderer.MarkSpriteDirty();
                    }
                }
            }
        }
        
        private void OnDayStarted( object sender, DayStartedEventArgs e )
        {
            // Enabled/disabled
            foreach ( var cp in contentPacks )
            {
                foreach ( var data in cp.Value.items )
                {
                    if ( data.Value.EnableConditionsObject == null )
                        data.Value.EnableConditionsObject = Mod.instance.cp.ParseConditions( Mod.instance.ModManifest,
                                                                                             data.Value.EnableConditions,
                                                                                             cp.Value.conditionVersion,
                                                                                             cp.Value.smapiPack.Manifest.Dependencies?.Select( ( d ) => d.UniqueID )?.ToArray() ?? new string[0] );

                    bool wasEnabled = data.Value.Enabled;
                    data.Value.Enabled = data.Value.EnableConditionsObject.IsMatch;
                    
                    if ( !data.Value.Enabled && wasEnabled )
                    {
                        data.Value.OnDisabled();
                    }
                }
                foreach ( var data in cp.Value.others )
                {
                    if ( data.EnableConditionsObject == null )
                        data.EnableConditionsObject = Mod.instance.cp.ParseConditions( Mod.instance.ModManifest,
                                                                                       data.EnableConditions,
                                                                                       cp.Value.conditionVersion,
                                                                                       cp.Value.smapiPack.Manifest.Dependencies?.Select( ( d ) => d.UniqueID )?.ToArray() ?? new string[ 0 ] );

                    data.Enabled = data.EnableConditionsObject.IsMatch;
                }
            }

            customMachineRecipes.Clear();
            customTailoringRecipes.Clear();

            // Dynamic fields
            foreach ( var cp in contentPacks )
            {
                var newItems = new Dictionary<string, CommonPackData>();
                foreach ( var data in cp.Value.items )
                {
                    var newItem = ( CommonPackData ) data.Value.original.Clone();
                    newItem.ApplyDynamicFields();
                    newItems.Add( data.Key, newItem );
                }
                cp.Value.items = newItems;

                var newOthers = new List<BasePackData>();
                foreach ( var data in cp.Value.others )
                {
                    var newOther = ( BasePackData ) data.original.Clone();
                    newOther.ApplyDynamicFields();
                    newOthers.Add( newOther );

                    if ( newOther is MachineRecipePackData machineRecipe )
                    {
                        if ( !customMachineRecipes.ContainsKey( machineRecipe.MachineId ) )
                            customMachineRecipes.Add( machineRecipe.MachineId, new List<MachineRecipePackData>() );
                        if ( machineRecipe.Enabled )
                            customMachineRecipes[ machineRecipe.MachineId ].Add( machineRecipe );
                    }
                    else if ( newOther is TailoringRecipePackData tailoringRecipe )
                    {
                        if ( tailoringRecipe.Enabled )
                            customTailoringRecipes.Add( tailoringRecipe );
                    }
                }
                cp.Value.others = newOthers;
            }

            foreach ( var player in Game1.getAllFarmers() )
            {
                foreach ( var recipe in customCraftingRecipes )
                {
                    bool learn = false;
                    if ( recipe.data.KnownByDefault )
                        learn = true;
                    if ( recipe.data.SkillUnlockName != null && recipe.data.SkillUnlockLevel > 0 )
                    {
                        int level = 0;
                        switch ( recipe.data.SkillUnlockName )
                        {
                            case "Farming": level = Game1.player.farmingLevel.Value; break;
                            case "Fishing": level = Game1.player.fishingLevel.Value; break;
                            case "Foraging": level = Game1.player.foragingLevel.Value; break;
                            case "Mining": level = Game1.player.miningLevel.Value; break;
                            case "Combat": level = Game1.player.combatLevel.Value; break;
                            case "Luck": level = Game1.player.luckLevel.Value; break;
                            default: level = Game1.player.GetCustomSkillLevel( recipe.data.SkillUnlockName ); break;
                        }

                        if ( level >= recipe.data.SkillUnlockLevel )
                            learn = true;
                    }

                    if ( learn )
                    {
                        if ( !recipe.data.IsCooking && !player.craftingRecipes.Keys.Contains( recipe.data.CraftingDataKey ) )
                            player.craftingRecipes.Add( recipe.data.CraftingDataKey, 0 );
                        else if ( !recipe.data.IsCooking && !player.cookingRecipes.Keys.Contains( recipe.data.CraftingDataKey ) )
                            player.cookingRecipes.Add( recipe.data.CraftingDataKey, 0 );
                    }
                }
            }

            RefreshRecipes();
            RefreshShopEntries();

            if ( Context.ScreenId == 0 )
            {
                RefreshSpritebatchCache();
            }

            Helper.Content.InvalidateCache("Data\\CraftingRecipes");
            Helper.Content.InvalidateCache("Data\\CookingRecipes");
        }

        private void OnMenuChanged( object sender, MenuChangedEventArgs e )
        {
            if ( e.NewMenu is ShopMenu shop )
            {
                if ( shop.storeContext == "ResortBar" || shop.storeContext == "VolcanoShop" )
                {
                    PatchCommon.DoShop( shop.storeContext, shop );
                }
            }
        }

        private void OnListCommand( string cmd, string[] args )
        {
            string output = "";
            foreach ( var cp in contentPacks )
            {
                output += cp.Key + ":\n";
                foreach ( var entry in cp.Value.items )
                {
                    output += "\t" + entry.Key + "\n";
                }
                output += "\n";
            }

            Log.Info( output );
        }

        private void OnAddCommand( string cmd, string[] args )
        {
            if ( args.Length < 1 )
            {
                Log.Info( "Usage: dga_add <mod.id/ItemId> [amount]" );
                return;
            }

            var data = Find( args[ 0 ] );
            if ( data == null )
            {
                Log.Error( $"Item '{args[ 0 ]}' not found." );
                return;
            }

            var item = data.ToItem();
            if ( item == null )
            {
                Log.Error( $"The item '{args[ 0 ]}' has no inventory form." );
                return;
            }
            if ( args.Length >= 2 )
            {
                item.Stack = int.Parse( args[ 1 ] );
            }

            Game1.player.addItemByMenuIfNecessary( item );
        }

        private string[] AddCommandAutoComplete( string cmd, string input )
        {
            if ( input.Contains( ' ' ) )
                return null;

            var ret = new List<string>();

            int slash = input.IndexOf( '/' );
            if ( slash == -1 )
            {
                foreach ( string packId in contentPacks.Keys )
                {
                    if ( packId.StartsWith( input ) )
                        ret.Add( packId );
                }
            }
            else
            {
                string packId = input.Substring( 0, slash );
                string itemInPack = input.Substring( slash + 1 );

                if ( !contentPacks.ContainsKey( packId ) )
                    return null;

                var pack = contentPacks[ packId ];
                foreach ( string itemId in pack.items.Keys )
                {
                    if ( itemId.StartsWith( itemInPack ) )
                        ret.Add( packId + "/" + itemId.Replace( " ", "\" \"" ) );
                }
            }

            return ret.ToArray();
        }

        private void OnForceCommand( string cmd, string[] args )
        {
            OnDayStarted( this, null );
        }

        private void OnReloadCommand( string cmd, string[] args )
        {
            contentPacks.Clear();
            itemLookup.Clear();
            foreach ( var recipe in customCraftingRecipes )
                ( recipe.data.IsCooking ? SpaceCore.CustomCraftingRecipe.CookingRecipes : SpaceCore.CustomCraftingRecipe.CraftingRecipes ).Remove( recipe.data.CraftingDataKey );
            customCraftingRecipes.Clear();
            customForgeRecipes.Clear();
            foreach ( var state in _state.GetActiveValues() )
            {
                state.Value.TodaysShopEntries.Clear();
            }
            LoadContentPacks();
            OnDayStarted( this, null );
        }
        /*
        private string[] ReloadCommandAutoComplete( string cmd, string input )
        {
            if ( input.Contains( ' ' ) )
                return null;

            var ret = new List<string>();

            foreach ( string packId in contentPacks.Keys )
            {
                if ( packId.StartsWith( input ) )
                    ret.Add( packId );
            }

            return ret.ToArray();
        }*/

        public void OnCleanCommand( string cmd, string[] args )
        {
            SpaceUtility.iterateAllItems( ( item ) =>
            {
                if ( item is IDGAItem citem && Mod.Find( citem.FullId ) == null )
                {
                    return null;
                }
                return item;
            } );
            SpaceUtility.iterateAllTerrainFeatures( ( tf ) =>
            {
                if ( tf is IDGAItem citem && Mod.Find( citem.FullId ) == null )
                {
                    return null;
                }
                else if ( tf is HoeDirt hd && hd.crop is IDGAItem citem2 && Mod.Find( citem2.FullId ) == null )
                {
                    hd.crop = null;
                }
                return tf;
            } );
        }

        public static void AddContentPack( IManifest manifest, string dir )
        {
            Log.Debug( $"Loading fake content pack for \"{manifest.Name}\"..." );
            if ( manifest.ExtraFields == null ||
                 !manifest.ExtraFields.ContainsKey( "DGA.FormatVersion" ) ||
                 !int.TryParse( manifest.ExtraFields[ "DGA.FormatVersion" ].ToString(), out int ver ) )
            {
                Log.Error( "Must specify a DGA.FormatVersion as an integer! (See documentation.)" );
                return;
            }
            if ( ver != 1 )
            {
                Log.Error( "Unsupported format version!" );
                return;
            }
            if ( !manifest.ExtraFields.ContainsKey( "DGA.ConditionsFormatVersion" ) ||
                 !SemanticVersion.TryParse( manifest.ExtraFields[ "DGA.ConditionsFormatVersion" ].ToString(), out ISemanticVersion condVer ) )
            {
                Log.Error( "Must specify a DGA.ConditionsFormatVersion as a semantic version! (See documentation.)" );
                return;
            }

            var cp = Mod.instance.Helper.ContentPacks.CreateTemporary( dir, manifest.UniqueID, manifest.Name, manifest.Description, manifest.Author, manifest.Version );
            var pack = new ContentPack( cp, condVer );
            contentPacks.Add( manifest.UniqueID, pack );
        }

        private void LoadContentPacks()
        {
            foreach ( var cp in Helper.ContentPacks.GetOwned() )
            {
                Log.Debug( $"Loading content pack \"{cp.Manifest.Name}\"..." );
                if ( cp.Manifest.ExtraFields == null ||
                     !cp.Manifest.ExtraFields.ContainsKey( "DGA.FormatVersion" ) ||
                     !int.TryParse( cp.Manifest.ExtraFields[ "DGA.FormatVersion" ].ToString(), out int ver ) )
                {
                    Log.Error("Must specify a DGA.FormatVersion as an integer! (See documentation.)");
                    continue;
                }
                if ( ver != 1 )
                {
                    Log.Error( "Unsupported format version!" );
                    continue;
                }
                if ( !cp.Manifest.ExtraFields.ContainsKey( "DGA.ConditionsFormatVersion" ) ||
                     !SemanticVersion.TryParse( cp.Manifest.ExtraFields[ "DGA.ConditionsFormatVersion" ].ToString(), out ISemanticVersion condVer ) )
                {
                    Log.Error( "Must specify a DGA.ConditionsFormatVersion as a semantic version! (See documentation.)" );
                    continue;
                }
                var pack = new ContentPack( cp );
                contentPacks.Add( cp.Manifest.UniqueID, pack );

                foreach ( var recipe in pack.items.Values.OfType<CraftingRecipePackData>() )
                {
                    var crecipe = new DGACustomCraftingRecipe(recipe);
                    customCraftingRecipes.Add( crecipe );
                    ( recipe.IsCooking ? SpaceCore.CustomCraftingRecipe.CookingRecipes : SpaceCore.CustomCraftingRecipe.CraftingRecipes ).Add( recipe.CraftingDataKey, crecipe );
                }

                foreach ( var recipe in pack.others.OfType<ForgeRecipePackData>() )
                {
                    var crecipe = new DGACustomForgeRecipe(recipe);
                    customForgeRecipes.Add( crecipe );
                    CustomForgeRecipe.Recipes.Add( crecipe );
                }
            }
        }
        public bool CanLoad<T>( IAssetInfo asset )
        {
            foreach ( var pack in contentPacks )
            {
                if ( pack.Value.CanLoad<T>( asset ) )
                    return true;
            }

            return false;
        }

        public T Load<T>( IAssetInfo asset )
        {
            foreach ( var pack in contentPacks )
            {
                if ( pack.Value.CanLoad<T>( asset ) )
                    return pack.Value.Load< T >( asset );
            }

            return default( T );
        }

        public bool CanEdit<T>( IAssetInfo asset )
        {
            if (asset.AssetNameEquals("Data\\CookingRecipes"))
                return true;
            if (asset.AssetNameEquals("Data\\CraftingRecipes"))
                return true;
            if (asset.AssetNameEquals("Data\\ObjectInformation"))
                return true;
            return false;
        }

        public void Edit<T>( IAssetData asset )
        {
            if (asset.AssetNameEquals("Data\\CookingRecipes"))
            {
                var dict = asset.AsDictionary<string, string>().Data;
                int i = 0;
                foreach (var crecipe in customCraftingRecipes)
                {
                    if (crecipe.data.Enabled && crecipe.data.IsCooking)
                    {
                        dict.Add(crecipe.data.CraftingDataKey, crecipe.data.CraftingDataValue);
                        ++i;
                    }
                }
                Log.Trace("Added " + i + "/" + customCraftingRecipes.Count + " entries to cooking recipes");
            }
            else if (asset.AssetNameEquals("Data\\CraftingRecipes"))
            {
                var dict = asset.AsDictionary<string, string>().Data;
                int i = 0;
                foreach (var crecipe in customCraftingRecipes)
                {
                    if (crecipe.data.Enabled && !crecipe.data.IsCooking)
                    {
                        dict.Add(crecipe.data.CraftingDataKey, crecipe.data.CraftingDataValue);
                        ++i;
                    }
                }
                Log.Trace("Added " + i + "/" + customCraftingRecipes.Count + " entries to crafting recipes");
            }
            else if (asset.AssetNameEquals("Data\\ObjectInformation"))
            {
                asset.AsDictionary<int, string>().Data.Add(BaseFakeObjectId, "DGA Dummy Object/0/0/Basic -20/DGA Dummy Object/You shouldn't have this./food/0 0 0 0 0 0 0 0 0 0 0 0/0");
            }
        }

        /*
        private Item MakeItemFrom( string name, ContentPack context = null )
        {
            if ( context != null )
            {
                foreach ( var item in context.items )
                {
                    if ( name == item.Key )
                    {
                        var retCtx = item.Value.ToItem();
                        if ( retCtx != null )
                            return retCtx;
                    }
                }
            }

            int slash = name.IndexOf( '/' );
            if ( slash != -1 )
            {
                string pack = name.Substring( 0, slash );
                string item = name.Substring( slash + 1 );
                if ( contentPacks.ContainsKey( pack ) && contentPacks[ pack ].items.ContainsKey( item ) )
                {
                    var retCp = contentPacks[ pack ].items[ item ].ToItem();
                    if ( retCp != null )
                        return retCp;
                }

                Log.Error( $"Failed to find item \"{name}\" from context {context?.smapiPack?.Manifest?.UniqueID}" );
                return null;
            }

            var ret = Utility.getItemFromStandardTextDescription( name, Game1.player );
            if ( ret == null )
            {
                Log.Error( $"Failed to find item \"{name}\" from context {context?.smapiPack?.Manifest?.UniqueID}" );

            }
            return ret;
        }
        */

        private void RefreshRecipes()
        {
            foreach ( var recipe in customCraftingRecipes )
                recipe.Refresh();
            foreach ( var recipe in customForgeRecipes )
                recipe.Refresh();
        }

        private void RefreshShopEntries()
        {
            State.TodaysShopEntries.Clear();
            foreach ( var cp in contentPacks )
            {
                foreach ( var shopEntry in cp.Value.others.OfType< ShopEntryPackData >() )
                {
                    if ( shopEntry.Enabled )
                    {
                        if ( !State.TodaysShopEntries.ContainsKey( shopEntry.ShopId ) )
                            State.TodaysShopEntries.Add( shopEntry.ShopId, new List<ShopEntry>() );
                        State.TodaysShopEntries[ shopEntry.ShopId ].Add( new ShopEntry()
                        {
                            Item = shopEntry.Item.Create(),//MakeItemFrom( shopEntry.Item, cp.Value ),
                            Quantity = shopEntry.MaxSold,
                            Price = shopEntry.Cost,
                            CurrencyId = shopEntry.Currency == null ? null : (int.TryParse( shopEntry.Currency, out int intCurr ) ? intCurr : $"{cp.Key}/{shopEntry.Currency}".GetDeterministicHashCode())
                        } );
                    }
                }
            }
        }

        internal void RefreshSpritebatchCache()
        {
            if ( Game1.objectSpriteSheet == null )
                Game1.objectSpriteSheet = Game1.content.Load< Texture2D >( "Maps\\springobjects" );

            SpriteBatchTileSheetAdjustments.objectOverrides.Clear();
            SpriteBatchTileSheetAdjustments.weaponOverrides.Clear();
            SpriteBatchTileSheetAdjustments.hatOverrides.Clear();
            SpriteBatchTileSheetAdjustments.shirtOverrides.Clear();
            SpriteBatchTileSheetAdjustments.pantsOverrides.Clear();
            foreach ( var cp in contentPacks )
            {
                foreach ( var item in cp.Value.items.Values )
                {
                    if ( item is ObjectPackData obj )
                    {
                        var tex = cp.Value.GetTexture( obj.Texture, 16, 16 );
                        string fullId = $"{cp.Key}/{obj.ID}";
                        SpriteBatchTileSheetAdjustments.objectOverrides.Add( Game1.getSourceRectForStandardTileSheet( Game1.objectSpriteSheet, fullId.GetDeterministicHashCode(), 16, 16 ), tex );
                    }
                    else if ( item is MeleeWeaponPackData weapon )
                    {
                        var tex = cp.Value.GetTexture( weapon.Texture, 16, 16 );
                        string fullId = $"{cp.Key}/{weapon.ID}";
                        SpriteBatchTileSheetAdjustments.weaponOverrides.Add( Game1.getSourceRectForStandardTileSheet( Tool.weaponsTexture, fullId.GetDeterministicHashCode(), 16, 16 ), tex );
                    }
                    else if ( item is HatPackData hat )
                    {
                        var tex = hat.GetTexture();
                        if ( !tex.Rect.HasValue )
                            tex.Rect = new Rectangle( 0, 0, tex.Texture.Width, tex.Texture.Height );

                        string fullId = $"{cp.Key}/{hat.ID}";
                        int which = fullId.GetDeterministicHashCode();

                        var rect = new Rectangle(20 * (int)which % FarmerRenderer.hatsTexture.Width, 20 * (int)which / FarmerRenderer.hatsTexture.Width * 20 * 4, 20, 20);
                        SpriteBatchTileSheetAdjustments.hatOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 0, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 20 );
                        SpriteBatchTileSheetAdjustments.hatOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 1, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 20 );
                        SpriteBatchTileSheetAdjustments.hatOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 2, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 20 );
                        SpriteBatchTileSheetAdjustments.hatOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 3, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                    }
                    else if ( item is ShirtPackData shirt )
                    {
                        string fullId = $"{cp.Key}/{shirt.ID}";
                        int which = fullId.GetDeterministicHashCode();

                        var tex = cp.Value.GetTexture( shirt.TextureMale, 8, 32 );
                        if ( !tex.Rect.HasValue )
                            tex.Rect = new Rectangle( 0, 0, tex.Texture.Width, tex.Texture.Height );
                        var rect = new Rectangle( which * 8 % 128, which * 8 / 128 * 32, 8, 8);
                        SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 0, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 8 );
                        SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 1, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 8 );
                        SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 2, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        rect.Offset( 0, 8 );
                        SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 3, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );

                        if ( shirt.TextureMaleColor != null )
                        {
                            tex = cp.Value.GetTexture( shirt.TextureMaleColor, 8, 32 );
                            if ( !tex.Rect.HasValue )
                                tex.Rect = new Rectangle( 0, 0, tex.Texture.Width, tex.Texture.Height );
                            rect.Offset( 128, -24 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 0, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 1, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 2, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 3, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        }

                        if ( shirt.TextureFemale != null )
                        {
                            tex = cp.Value.GetTexture( shirt.TextureFemale, 8, 32 );
                            if ( !tex.Rect.HasValue )
                                tex.Rect = new Rectangle( 0, 0, tex.Texture.Width, tex.Texture.Height );
                            which += 1;
                            rect = new Rectangle( which * 8 % 128, which * 8 / 128 * 32, 8, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 0, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 1, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 2, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 3, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        }

                        if ( shirt.TextureFemaleColor != null )
                        {
                            tex = cp.Value.GetTexture( shirt.TextureFemaleColor, 8, 32 );
                            if ( !tex.Rect.HasValue )
                                tex.Rect = new Rectangle( 0, 0, tex.Texture.Width, tex.Texture.Height );
                            rect.Offset( 128, -24 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 0, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 1, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 2, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                            rect.Offset( 0, 8 );
                            SpriteBatchTileSheetAdjustments.shirtOverrides.Add( rect, new TexturedRect() { Texture = tex.Texture, Rect = new Rectangle( tex.Rect.Value.X, tex.Rect.Value.Y + tex.Rect.Value.Height / 4 * 3, tex.Rect.Value.Width, tex.Rect.Value.Height / 4 ) } );
                        }
                    }
                    else if ( item is PantsPackData pants )
                    {
                        var tex = cp.Value.GetTexture( pants.Texture, 192, 688 );
                        string fullId = $"{cp.Key}/{pants.ID}";
                        int which = fullId.GetDeterministicHashCode();
                        SpriteBatchTileSheetAdjustments.pantsOverrides.Add( new Rectangle( which % 10 * 192, which / 10 * 688, 192, 688 ), tex );
                    }
                }
            }
        }
    }
}
