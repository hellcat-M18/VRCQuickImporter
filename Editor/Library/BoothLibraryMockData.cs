using System.Collections.Generic;

namespace VRCQuickImporter.Editor.Library
{
    /// <summary>
    /// ライブラリUIの見た目確認用サンプルデータ。
    /// 実データ取得（BOOTH同期）は後フェーズで実装するため、現状は固定サンプルのみ。
    /// BOOTH本体のスキリスト風の見え方を再現するため、カテゴリ/バッジ/価格/いいねを含む。
    /// </summary>
    internal static class BoothLibraryMockData
    {
        public static List<BoothProduct> CreateSampleProducts()
        {
            return new List<BoothProduct>
            {
                new BoothProduct
                {
                    ProductId = "mock-001",
                    Name = "サンプル衣装セットA",
                    ShopName = "サンプルショップ",
                    CategoryLabel = "3D衣装",
                    BadgeText = "VRCHAT",
                    PriceText = "¥3,300",
                    LikeCount = 1284,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f1",
                            Name = "CostumeA.unitypackage",
                            SizeText = "48.2 MB",
                            Kind = BoothDownloadFileKind.UnityPackage
                        },
                        new BoothDownloadFile
                        {
                            FileId = "f2",
                            Name = "CostumeA_PSD.zip",
                            SizeText = "120.5 MB",
                            Kind = BoothDownloadFileKind.Zip
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-002",
                    Name = "サンプル3Dキャラクターモデル",
                    ShopName = "アバター工房",
                    CategoryLabel = "3Dキャラクター",
                    BadgeText = "VRCHAT",
                    PriceText = "¥9,900",
                    LikeCount = 3210,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f3",
                            Name = "CharacterModel.zip",
                            SizeText = "320.0 MB",
                            Kind = BoothDownloadFileKind.Zip
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-003",
                    Name = "サンプルテクスチャ素材集 4K/2K",
                    ShopName = "TextureLab",
                    CategoryLabel = "テクスチャ",
                    BadgeText = "VRCHAT",
                    PriceText = "¥1,650",
                    LikeCount = 542,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f4",
                            Name = "Textures_4K.zip",
                            SizeText = "64.7 MB",
                            Kind = BoothDownloadFileKind.Zip
                        },
                        new BoothDownloadFile
                        {
                            FileId = "f5",
                            Name = "Textures_2K.zip",
                            SizeText = "22.1 MB",
                            Kind = BoothDownloadFileKind.Zip
                        },
                        new BoothDownloadFile
                        {
                            FileId = "f6",
                            Name = "Preview.png",
                            SizeText = "2.3 MB",
                            Kind = BoothDownloadFileKind.Image
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-004",
                    Name = "サンプルへアスタイル 3種セット",
                    ShopName = "HairStudio",
                    CategoryLabel = "3D衣装",
                    BadgeText = "VRCHAT",
                    PriceText = "¥2,200",
                    LikeCount = 876,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f7",
                            Name = "HairSet.unitypackage",
                            SizeText = "35.8 MB",
                            Kind = BoothDownloadFileKind.UnityPackage
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-005",
                    Name = "サンプルアクセサリ詰め合わせ",
                    ShopName = "AccessoryBox",
                    CategoryLabel = "3Dアクセサリ",
                    BadgeText = "VRCHAT",
                    PriceText = "無料",
                    LikeCount = 4102,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f8",
                            Name = "Accessories.zip",
                            SizeText = "12.4 MB",
                            Kind = BoothDownloadFileKind.Zip
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-006",
                    Name = "サンプルエフェクト/パーティクル集",
                    ShopName = "EffectForge",
                    CategoryLabel = "ツール/ソフトウェア",
                    BadgeText = "VRCHAT",
                    PriceText = "¥4,950",
                    LikeCount = 199,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f9",
                            Name = "Effects.unitypackage",
                            SizeText = "88.0 MB",
                            Kind = BoothDownloadFileKind.UnityPackage
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-007",
                    Name = "サンプルメッシュ改造用ベースモデル",
                    ShopName = "BaseWorks",
                    CategoryLabel = "3Dキャラクター",
                    BadgeText = "VRCHAT",
                    PriceText = "¥6,600",
                    LikeCount = 753,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f10",
                            Name = "BaseModel.zip",
                            SizeText = "150.2 MB",
                            Kind = BoothDownloadFileKind.Zip
                        },
                        new BoothDownloadFile
                        {
                            FileId = "f11",
                            Name = "BaseModel_Blend.zip",
                            SizeText = "44.0 MB",
                            Kind = BoothDownloadFileKind.Zip
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-008",
                    Name = "サンプル立ち絵/UI素材パック",
                    ShopName = "ArtAndUI",
                    CategoryLabel = "イラスト",
                    BadgeText = "VRCHAT",
                    PriceText = "¥1,100",
                    LikeCount = 318,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f12",
                            Name = "UIPack.zip",
                            SizeText = "18.6 MB",
                            Kind = BoothDownloadFileKind.Zip
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-009",
                    Name = "サンプル演出用アニメーションセット",
                    ShopName = "MotionKit",
                    CategoryLabel = "3Dキャラクター",
                    BadgeText = "VRCHAT",
                    PriceText = "¥3,850",
                    LikeCount = 612,
                    Files =
                    {
                        new BoothDownloadFile
                        {
                            FileId = "f13",
                            Name = "Animations.unitypackage",
                            SizeText = "27.9 MB",
                            Kind = BoothDownloadFileKind.UnityPackage
                        }
                    }
                },
                new BoothProduct
                {
                    ProductId = "mock-010",
                    Name = "ダウンロードファイル無しのサンプル商品",
                    ShopName = "EmptyShop",
                    CategoryLabel = "その他",
                    BadgeText = "VRCHAT",
                    PriceText = "—",
                    LikeCount = 0,
                    Files = new List<BoothDownloadFile>()
                }
            };
        }
    }
}
