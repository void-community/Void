# Changelog

## [0.6.1](https://github.com/void-community/Void/compare/v0.6.0...v0.6.1) (2026-09-26)


### Bug Fixes

* **deps:** ⬆️ update dependency @astrojs/starlight to ^0.42.0 ([9db850b](https://github.com/void-community/Void/commit/9db850b17e26fdd4ac55df48850430b225998b5f))
* **deps:** ⬆️ update dependency starlight-links-validator to ^0.26.0 ([419ee6a](https://github.com/void-community/Void/commit/419ee6a6478b1e47d42e52728e9e244000572419))


### Performance Improvements

* **client:** ⚡ accelerated version image publishing ([6271d91](https://github.com/void-community/Void/commit/6271d9193982fcbc05999cb4e039e5289e3677dc))
* **client:** ⚡ reduced offline image export duration ([25be96d](https://github.com/void-community/Void/commit/25be96de6d4f12c523b7e735d626ca3622455027))
* **client:** ⚡ shared application layers across client images ([7e7144d](https://github.com/void-community/Void/commit/7e7144d66994407b54fb82f03b31c8447ab397b1))

## [0.6.0](https://github.com/void-community/Void/compare/v0.5.17...v0.6.0) (2026-09-20)


### ⚠ BREAKING CHANGES

* **client:** replaced legacy client endpoints with /api/game/* routes.

### Features

* **api:** ✨ added chat input handling and configuration options ([47f5d5f](https://github.com/void-community/Void/commit/47f5d5ff2112cf3133b35fef6a4d04c3bc34cdd4))
* **api:** ✨ updated environment variables for PaperServer configuration ([8b612eb](https://github.com/void-community/Void/commit/8b612ebf9d9811c4e48e9d45a20593d4fd1d2de1))
* **authentication:** ✨ added temporary dimension handling for respawn on re-authentication ([e4331ca](https://github.com/void-community/Void/commit/e4331ca72bc3e954fcddd2295cfbbaed32ceb946))
* **client:** ✨ accepted an optional version for NeoForge launches ([072ffa6](https://github.com/void-community/Void/commit/072ffa6f79f14e7f79cd52e15cd7601837635329))
* **client:** ✨ added asynchronous log reading and improved cancellation handling ([b6ffaca](https://github.com/void-community/Void/commit/b6ffaca2fb10f9deb79fd5106fb9c1a06de4ac0f))
* **client:** ✨ added configurable CurseForge API base URL ([fe75964](https://github.com/void-community/Void/commit/fe7596497c5f897010f5cca174b7b23ce95a0cef))
* **client:** ✨ added live player API ([ddb5391](https://github.com/void-community/Void/commit/ddb53913e7f0649b7b477131c0f43e8004236df8))
* **client:** ✨ added memory-bounded launches ([bb55548](https://github.com/void-community/Void/commit/bb555481c6c9c7e3917f142ad587685108569689))
* **client:** ✨ added player rotations ([82df245](https://github.com/void-community/Void/commit/82df2454fdd92ba8f807a312a7069aea3720c3ee))
* **client:** ✨ added retained diagnostics and integration artifacts ([b36dd84](https://github.com/void-community/Void/commit/b36dd84af0b9124c7a4a2c3a12e012302c5a6b43))
* **client:** ✨ moved version preparation into API ([2677a83](https://github.com/void-community/Void/commit/2677a83989a675b108eb841236d967ef838be0ae))
* **client:** ✨ namespaced API and noVNC routes ([98e2d31](https://github.com/void-community/Void/commit/98e2d31a3ec63fa6b7b9d663d7eb62b264c007f2))
* **client:** ✨ rebuilt the reusable game API ([7a2390e](https://github.com/void-community/Void/commit/7a2390e391d6f10e7b091676b6226d9cfb51f3dc))
* **client:** ✨ unified API and noVNC on port 80 ([ad65100](https://github.com/void-community/Void/commit/ad65100a810df4a1d739574d3f6f0a15f6abc95d))
* **client:** ✨ unified browser and API runtime ([ceda4d7](https://github.com/void-community/Void/commit/ceda4d76981df94043af418331b8cf95b19cd52f))
* **docker:** ✨ added multi-architecture support and improved Java installation process ([3da610f](https://github.com/void-community/Void/commit/3da610f4ce0a04f967f3e318ebbe33dccb399a5f))
* **java:** ✨ added script to execute Java with QEMU for i686 architecture ([37b7a84](https://github.com/void-community/Void/commit/37b7a84509fdb543040388d1748bd9d03816441f))
* **link:** ✨ improved async connection handling ([5c3d3e9](https://github.com/void-community/Void/commit/5c3d3e95ff7b496ee1183e3b4b8ecc31509a30d9))
* **list-minecraft-release-versions:** ✨ added support for specifying last release version ([9b968e5](https://github.com/void-community/Void/commit/9b968e5d5bd2fdda3285749da432d8ad22831797))
* **portablemc:** ✨ added support for LWJGL fixes based on Minecraft version ([7e6588c](https://github.com/void-community/Void/commit/7e6588c4bde159e3fae00c289bdfda74b386bf1c))
* **protocol:** ✨ added 26.2 login session IDs ([5adf839](https://github.com/void-community/Void/commit/5adf83966156069ea8ce005ebfb57afb39b7dfac))
* **protocol:** ✨ added Minecraft 26.2 definitions ([428c9ef](https://github.com/void-community/Void/commit/428c9ef65db78a7bc59efb0270e53405f3c643a1))
* **protocol:** ✨ added Minecraft 26.3 definitions ([cb9d74c](https://github.com/void-community/Void/commit/cb9d74cdb12e4d8ed40d4245f104e71a0dd1c8a9))
* **uuid:** ✨ added Guid-shaped Java UUID API ([05865dd](https://github.com/void-community/Void/commit/05865ddd5e16b0a6c52d5823f4a0d9444899172d))


### Bug Fixes

* **api:** 🐛 corrected initialization and disposal of PortableMinecraftClient ([b0a49f0](https://github.com/void-community/Void/commit/b0a49f0655f95c38ba0d90f305056054da71d7ef))
* **client:** 🐛 accelerated OCR connection handling ([2e7df89](https://github.com/void-community/Void/commit/2e7df89a94f2c3a7575868663076d9eea1efc5c3))
* **client:** 🐛 acknowledged server address selection ([cd354c0](https://github.com/void-community/Void/commit/cd354c0dcd8de7fb3dbae4ac1b6566bbd859ecc6))
* **client:** 🐛 added fast OCR fallback ([4245f4f](https://github.com/void-community/Void/commit/4245f4f649c3789f04716a1e79d55fb42a7995df))
* **client:** 🐛 bounded OCR navigation loops ([f8c0770](https://github.com/void-community/Void/commit/f8c07702214d33107ec86dc1344b68e944e5b61a))
* **client:** 🐛 cleared legacy server addresses deterministically ([5bdde8e](https://github.com/void-community/Void/commit/5bdde8e87a90c8633da1f23b2fb5a81ec8e9e3b9))
* **client:** 🐛 corrected native automation lifecycle ([59b7719](https://github.com/void-community/Void/commit/59b77199ce389fc0cc8f9ad082df0f0874bc777a))
* **client:** 🐛 deferred automation until overlays cleared ([f3f768d](https://github.com/void-community/Void/commit/f3f768d3d2802a4ddaeda0569d27fdde88b47986))
* **client:** 🐛 disabled unstable oneDNN OCR inference ([3359e8f](https://github.com/void-community/Void/commit/3359e8fe95755b9875ae8dc2e267f75260aeea97))
* **client:** 🐛 distinguished self screen transitions ([5c19054](https://github.com/void-community/Void/commit/5c19054f5cd40b92d446426ae01d8bb211007d9c))
* **client:** 🐛 eliminated redundant image conversion ([d0f283a](https://github.com/void-community/Void/commit/d0f283a1ad646cc1595f93f6405bb9aa4f7b6f1f))
* **client:** 🐛 excluded the launcher splash from game readiness ([2d8e436](https://github.com/void-community/Void/commit/2d8e436097011e8457de91a0ddf3ed470bef86c1))
* **client:** 🐛 isolated chat image test seam ([16b0ec2](https://github.com/void-community/Void/commit/16b0ec25e068e09915264a88f2943aedeca6e246))
* **client:** 🐛 made client API actions respond immediately ([c971a58](https://github.com/void-community/Void/commit/c971a58c82ceb4f51bcb0ccffb787c980b5e124a))
* **client:** 🐛 made server connection navigation idempotent ([7f2b978](https://github.com/void-community/Void/commit/7f2b9786580a2ee58dd84d219fab686b1a5ce8b9))
* **client:** 🐛 ordered server address replacement ([daae3f9](https://github.com/void-community/Void/commit/daae3f90cb6dccd4e18d959c7f89ea88cb064146))
* **client:** 🐛 prevented executor dispatch re-entry ([f4e5604](https://github.com/void-community/Void/commit/f4e5604836a1f1db6c7277c8a355f518c7cf9a5a))
* **client:** 🐛 prevented stale Netty read disables from stalling connections ([7681097](https://github.com/void-community/Void/commit/7681097ea4430109bd2f2a0ac0dab341647803ab))
* **client:** 🐛 reduced repeated OCR scans ([462795e](https://github.com/void-community/Void/commit/462795eaa03519acc0e73a8e5b158bcc69cfd0b8))
* **client:** 🐛 repaired dynamic multiplayer detection ([cdc4a49](https://github.com/void-community/Void/commit/cdc4a498a5fd2e72cbdbb9128607ddd182de0cbb))
* **client:** 🐛 replaced desktop input automation ([998ca2a](https://github.com/void-community/Void/commit/998ca2a400b6871921718f68d9b1d9dc6da57c70))
* **client:** 🐛 replaced legacy i386 emulation ([0532717](https://github.com/void-community/Void/commit/05327172ecb1bec47261c5f329c7a1b0a85a6570))
* **client:** 🐛 replaced server address automation ([b814d0e](https://github.com/void-community/Void/commit/b814d0eefba86ebfeadafad4141927fc12695e09))
* **client:** 🐛 resolved inherited agent methods ([267cbcb](https://github.com/void-community/Void/commit/267cbcbf988276cfbcf3b0ab39e408901a8c696f))
* **client:** 🐛 restored legacy native installation on ARM64 ([78eab9b](https://github.com/void-community/Void/commit/78eab9b5ef7fb4e44c2dd7dd686cb6c65d416900))
* **client:** 🐛 restored portable OCR connection handling ([ca08889](https://github.com/void-community/Void/commit/ca08889acc51d7274c136acaaf199af7f4089956))
* **client:** 🐛 restored settings and corrected diagnostics synchronization ([f52bf25](https://github.com/void-community/Void/commit/f52bf25204651079132f2dc273f217ab802c1732))
* **client:** 🐛 retried portablemc dry-run downloads ([acb669a](https://github.com/void-community/Void/commit/acb669a9195736b5652624edcadeb0b714256019))
* **client:** 🐛 stabilized OCR integration navigation ([9482cbe](https://github.com/void-community/Void/commit/9482cbeb40ac1ef31791b6862a567b7820cba174))
* **client:** 🐛 stabilized overloaded navigation ([3404064](https://github.com/void-community/Void/commit/34040644719bb755bb6cec841393e4782c0b863e))
* **client:** 🐛 stabilized visual button retries ([6a17258](https://github.com/void-community/Void/commit/6a172589a8b192d76d3e028a5174e87094b93c11))
* **client:** 🐛 supported SDL-based Minecraft 26.3 clients ([8e58425](https://github.com/void-community/Void/commit/8e584255bdc044a28c97b6b2993a8d9764fff8b3))
* **client:** 🐛 surfaced and recovered client failures ([9da2751](https://github.com/void-community/Void/commit/9da27518a65fe16079f4da3a776873f74e9ed7cd))
* **client:** 🐛 switched navigation to OCR targets ([dc060d4](https://github.com/void-community/Void/commit/dc060d4988b5ad2ad43b28dbadd96804369a64fc))
* **client:** 🐛 updated PortableMC launch resolution ([693e16c](https://github.com/void-community/Void/commit/693e16cb58996b9620f1a03513a582c22972256a))
* **client:** 🐛 verified pasted server addresses ([92233ea](https://github.com/void-community/Void/commit/92233eababef9b4e7b230f725f66595cf8082755))
* **client:** 🐛 waited for client stop before restart ([fbf5d88](https://github.com/void-community/Void/commit/fbf5d885099750ec2184e7547d258b78782f913c))
* **commands:** 🐛 lowered signed cancellation logging ([357511e](https://github.com/void-community/Void/commit/357511ee93196f8a829bdc89eb49bee68d440edc))
* **commands:** 🐛 rejected unavailable parser mappings ([5fc0d75](https://github.com/void-community/Void/commit/5fc0d75979346930e153acad9acc721ee2b12fdd))
* **common:** 🐛 corrected keep-alive timeout timing ([6ac879c](https://github.com/void-community/Void/commit/6ac879c67ea00b15eedfad669a21824b76baf33d))
* **common:** 🐛 serialized keep-alive requests ([b8f4edd](https://github.com/void-community/Void/commit/b8f4edd965033e05e189bc2afe4708b2f888c011))
* **console:** 🐛 waited for shutdown without terminal ([8454f55](https://github.com/void-community/Void/commit/8454f5533ecdd08cd1cd3e046e7f04d6c9b0155e))
* **demo:** 🐛 repaired interactive demo startup ([3500ce5](https://github.com/void-community/Void/commit/3500ce5c18b88b7d2896741db74fa66962882905))
* **deps:** update dependency @astrojs/starlight to ^0.40.0 ([99b7c4a](https://github.com/void-community/Void/commit/99b7c4aecb4069ced4522f66984074ec5c12748a))
* **deps:** update dependency @astrojs/starlight to ^0.41.0 ([90c0f41](https://github.com/void-community/Void/commit/90c0f41ac785f12b23b9b1a6bdbf38afe2d40bc4))
* **deps:** update dependency astro to v7 ([8bf9fe9](https://github.com/void-community/Void/commit/8bf9fe9491866fe6be9ff1f95037fd078f37a72f))
* **deps:** update dependency sharp to ^0.35.0 ([14d2d10](https://github.com/void-community/Void/commit/14d2d10d3fb51f228716c32c2624e181bdc5f1ac))
* **deps:** update dependency starlight-links-validator to ^0.25.0 ([b2a1707](https://github.com/void-community/Void/commit/b2a170738f4ef782087b2387bc387799eab2d768))
* **docs:** 🐛 escaped closing details tag in JS template literal to fix MDX build ([9748f4a](https://github.com/void-community/Void/commit/9748f4a9c0f4cca4a7bc05fc453bbc5cb7ab2926))
* **integration:** 🧪 added real Paper kick coverage ([a13f75a](https://github.com/void-community/Void/commit/a13f75abcfce24ba803a27e1f39b345bee220ea7))
* **lifecycle:** 🐛 fixed legacy keep-alive ids ([c638524](https://github.com/void-community/Void/commit/c638524d5aec0482d3d74b38a1ffd56b6fd6b38b))
* **lifecycle:** 🐛 made keep-alive redirection deterministic ([e74bc35](https://github.com/void-community/Void/commit/e74bc352dc1988c48c4f1d828de38e44ee19b0aa))
* **lifecycle:** 🐛 paused keep-alives during redirects ([04febed](https://github.com/void-community/Void/commit/04febedaf0febe71fe4f64320171ea7f8dd6d7df))
* **lifecycle:** 🐛 preserved keep-alives across redirects ([6a0ba59](https://github.com/void-community/Void/commit/6a0ba59ec9fcf52d46623e7aa5875aa72169ae14))
* **lifecycle:** 🐛 reset keep-alive intervals on refresh ([9cce1a9](https://github.com/void-community/Void/commit/9cce1a926e8ff4e6d2f08d6f71d72cfb886a4187))
* **lifecycle:** 🐛 restored minimal keep-alive tracking ([e2a0d9b](https://github.com/void-community/Void/commit/e2a0d9b95c02736f1e9a7330ee68129894897df6))
* **link:** 🐛 changed states dictionary to concurrent implementation ([2ab72d3](https://github.com/void-community/Void/commit/2ab72d3ea6c5455bd77d236985b0ae19dea289bf))
* **links:** 🩹 raised forced shutdown timeout logs ([938c4a1](https://github.com/void-community/Void/commit/938c4a1dab6df979e55660fabd575db55620bc98))
* **network:** 🐛 made liveness checks non-destructive ([f3e21db](https://github.com/void-community/Void/commit/f3e21db1b2f8071e3b1f6ab331e97aa1fa4a22c6))
* **network:** 🐛 prevented redirect shutdown races ([1c101a9](https://github.com/void-community/Void/commit/1c101a99069c1795a7f667910ffd42e69ab330df))
* **network:** 🐛 restored raw TCP proxying ([0a0b8ae](https://github.com/void-community/Void/commit/0a0b8ae5f3147c7b70da8cd7fc1c07d61ed23e9d))
* **protocol:** 🐛 preserved entity ids across redirects ([ccd1c2a](https://github.com/void-community/Void/commit/ccd1c2a3cf6a7d559ac01c51e3cdba762bdc8faa))
* **protocol:** 🐛 refreshed entity ids across redirects ([e9b901d](https://github.com/void-community/Void/commit/e9b901d591959edf245183b743a505de10b40338))
* **protocol:** 🐛 stabilized server session IDs ([cbc3e9c](https://github.com/void-community/Void/commit/cbc3e9c49fd5ad8ee81cbe145c4e0d401aa65598))
* **registry:** 🐛 corrected packet handling for configuration phase in client and server ([868a2b4](https://github.com/void-community/Void/commit/868a2b42b3afd979532698731b128d9e65ae1d07))
* **registry:** 🐛 deferred transformation cleanup until links stopped ([ca24a04](https://github.com/void-community/Void/commit/ca24a04670eca78f32bbdb590e9b2363a92b39ff))
* **registry:** 🐛 preserved transformations across redirects ([b0b2a2d](https://github.com/void-community/Void/commit/b0b2a2d5e9d90261fb5d16feaf61772fee4fc2e2))
* **registry:** 🐛 use correct server channel on login ([46abccc](https://github.com/void-community/Void/commit/46abcccb87f5ec3ac87ef589a26bb63e4ad7f98b))
* **uuid:** 🐛 corrected Java-compatible UUID semantics ([d091405](https://github.com/void-community/Void/commit/d091405d056e416f3822fe24208bea6e3a33d96a))


### Performance Improvements

* **client:** ⚡ reduced startup memory and enabled native ARM64 legacy clients ([2274847](https://github.com/void-community/Void/commit/2274847caa40734b070b28208cf6a01c9fc5566e))


### Reverts

* **client:** ⏪ reverted OCR workflow changes ([3340b7e](https://github.com/void-community/Void/commit/3340b7e68eac334f3f0dac90f4745d604db2db5a))
* **integration:** ⏪ reverted process-based kick test ([21cb069](https://github.com/void-community/Void/commit/21cb0698b4f607d5d4ab1a512acdd8c69a2ce687))
* **protocol:** ⏪ reverted entity id rewriting ([ba83713](https://github.com/void-community/Void/commit/ba837137209dd2ae1533850c51d8cd9f636d3246))

## [0.5.17](https://github.com/caunt/Void/compare/v0.5.16...v0.5.17) (2026-05-31)


### Features

* **client:** ✨ added critical process crash handling for long-running child processes ([c71cc8d](https://github.com/caunt/Void/commit/c71cc8dec1c5667bc4bfed6f129a59e1c810c8d8))
* **client:** ✨ added ferium binary to Docker image ([02f6f09](https://github.com/caunt/Void/commit/02f6f09dbac8fc52d9ce45c2b29400c59ae1269f))
* **client:** ✨ added ferium modpack upgrade script with CurseForge fallback ([4d4fba0](https://github.com/caunt/Void/commit/4d4fba09abf4f5e1fafb8f69b780e2ab0c2bcee0))
* **client:** ✨ avoided repeated CurseForge reprovisioning ([6f633ba](https://github.com/caunt/Void/commit/6f633ba7fa8998e6521432948b2b0d83294cfecc))
* **client:** ✨ hardened curseforge launcher behavior ([f40ac1b](https://github.com/caunt/Void/commit/f40ac1b221a58c395c4f70d2c8ea621451e57959))
* **client:** ✨ replaced ferium upgrade launcher ([8aaefca](https://github.com/caunt/Void/commit/8aaefca3219c175baad8389cc64bc361c7948cc5))
* **client:** ✨ restored old shell script behavior in Api.cs ([d302022](https://github.com/caunt/Void/commit/d302022c440823b5d1246a2c37cc6ced2cc94437))
* **command:** ✨ added server suggestion functionality for command execution ([6fd8351](https://github.com/caunt/Void/commit/6fd8351e90598ce8e26e729b12998ed92206b982))
* **event:** ✨ added improved event handling with scoped listener resolution ([976aeeb](https://github.com/caunt/Void/commit/976aeebef2561b5053bf32e11de42ae2109674a6))
* extract PortableMinecraftClient Docker image into src/Client and publish to ghcr.io ([2f095ac](https://github.com/caunt/Void/commit/2f095ac6888c3b7eecc0d9572ee1f3e63a889269))
* **logging:** ✨ added player connection logging on server join ([3238718](https://github.com/caunt/Void/commit/323871892214d5032e6e24d1bc5d783210bc7090))
* **player:** ✨ added link validation for packet sending ([96fddb2](https://github.com/caunt/Void/commit/96fddb296efd7b453a0ce86ca731cf1818b11dcf))
* **server:** ✨ updated server initialization to use PaperServer and improved timeout handling ([cbaa899](https://github.com/caunt/Void/commit/cbaa899cbbb7f506c5790553a4aec09b56361942))
* **service:** ✨ improved plugin loading message formatting ([19613ae](https://github.com/caunt/Void/commit/19613ae03d37d3fe1fa6c42f8fe60ba3b27c8290))
* **uuid:** ✨ add implicit conversion operators for Guid ([fdd67b4](https://github.com/caunt/Void/commit/fdd67b4094a2dc6a5119caf5576af71287d8e0f1))


### Bug Fixes

* **AbstractLifecycleService, DependencyService, PlayerContext:** 🐛 corrected service context handling and improved method signatures ([d0543d9](https://github.com/caunt/Void/commit/d0543d9ff9a46bc8690288f525d3a6bb8903b2c7))
* **AbstractLifecycleService:** 🐛 corrected kick message handling logic ([b38b341](https://github.com/caunt/Void/commit/b38b341a7030187f8dd237f7fb123b02fe11c5b7))
* **api:** 🐛 added critical process management and cleanup on application stopping ([16a535c](https://github.com/caunt/Void/commit/16a535c6b8f504202eb525c86e19a68902ea20e0))
* **api:** 🐛 adjusted window position and size before screen capture ([e112efd](https://github.com/caunt/Void/commit/e112efdf98f64c0e478c8f2903ab2ee5e4379f6a))
* **api:** 🐛 corrected process start info initialization and improved argument handling ([0213ec7](https://github.com/caunt/Void/commit/0213ec7f0808fac8e0552750c406047a160fcecb))
* **api:** 🐛 corrected process start method and added new process start utility ([f050159](https://github.com/caunt/Void/commit/f050159dda94a49f9368a2b1eb3af0869562e3d6))
* **api:** 🐛 corrected variable naming conventions and improved argument handling ([2868c91](https://github.com/caunt/Void/commit/2868c91f13355f3be6136cb2c6bc3874e5018e4d))
* **api:** 🐛 corrected version parsing logic and improved documentation formatting ([5732e58](https://github.com/caunt/Void/commit/5732e589f254cb09c5aec4781501198f099c999c))
* **api:** 🐛 ensured chat window closure before brightness adjustment ([010f5e9](https://github.com/caunt/Void/commit/010f5e91cac2eeb124128274d1cb3ba8e18b3106))
* **api:** 🐛 removed redundant StartProcess method and streamlined process handling ([069dd75](https://github.com/caunt/Void/commit/069dd7580288c06f28a14616ff1bdfdf3f8a8364))
* **api:** 🐛 replaced SemaphoreSlim with AsyncLock for improved concurrency handling ([ef9e403](https://github.com/caunt/Void/commit/ef9e40366f021112e3de42a246d7ab7974b653b5))
* **AssemblyInfo:** 🐛 corrected max parallel threads configuration ([c68545e](https://github.com/caunt/Void/commit/c68545e82dbc7ecd1375dd625fef42b46aca924b))
* **client:** 🐛 added --clearmodifiers and --window flags to xdotool chat commands ([e72d821](https://github.com/caunt/Void/commit/e72d821da2b6ef6c3fad602a585c9e874f49b631))
* **client:** 🐛 appended download mappings and added temp file cleanup trap ([6d36474](https://github.com/caunt/Void/commit/6d3647418d476a01465b047db83d5d63b6d7b5eb))
* **client:** 🐛 corrected CurseForge download progress tracking ([1efab4c](https://github.com/caunt/Void/commit/1efab4cb7142cfb2ea6fd2c4f5ee3bcabe674475))
* **client:** 🐛 corrected StartCriticalProcess to set UseShellExecute and match FailFast message format ([203b0e4](https://github.com/caunt/Void/commit/203b0e49f288fecb7322d113624d9896a21eb022))
* **client:** 🐛 disabled case-insensitive property matching for manifest deserialization ([252c387](https://github.com/caunt/Void/commit/252c38708ccfda43fcc0ac724e79fea345910109))
* **client:** 🐛 enabled CurseForge launch argument passthrough ([6da1d19](https://github.com/caunt/Void/commit/6da1d1990e1b2dd5f1c8b8c0d725d264ec386c5e))
* **client:** 🐛 fixed Api.cs correctness issues and reverted src/Api changes ([4339c61](https://github.com/caunt/Void/commit/4339c611c3e555745d6260abe26d77e95c8e0695))
* **client:** 🐛 fixed category field index and removed redundant ferium re-run ([1c15b9b](https://github.com/caunt/Void/commit/1c15b9b06b20b80e6014d566cdcbf4a169e1c555))
* **client:** 🐛 fixed type ambiguity and brightness error handling in Api.cs ([05fec9d](https://github.com/caunt/Void/commit/05fec9d4d5b840e5fa3afcfe8da6d7606d03a2c2))
* **client:** 🐛 improved CurseForge startup logging and downloads ([9b63e8c](https://github.com/caunt/Void/commit/9b63e8c4e99d096281448f4725fe6307c8eef2d3))
* **client:** 🐛 materialized query string values to string array ([fab01cc](https://github.com/caunt/Void/commit/fab01cc2834458422e4aa911c90eab400eecc25d))
* **client:** 🐛 moved directory cleanup before overrides extraction ([89f1dd8](https://github.com/caunt/Void/commit/89f1dd86db29f02979185ebce5170d12d2457bf9))
* **client:** 🐛 prevented FailFast on intentional /stop-client kill ([62ca718](https://github.com/caunt/Void/commit/62ca7189b4e0ae1466377ab4c8010b90a9ff52dc))
* **client:** 🐛 replaced ConcurrentDictionary with lock-protected HashSet for expected exit tracking ([a2dfa2a](https://github.com/caunt/Void/commit/a2dfa2aa38a107572d8c31c6b834d0ab14e2e23f))
* **client:** 🔒 sanitised fileName with basename to prevent path traversal ([b677fc2](https://github.com/caunt/Void/commit/b677fc2fb2ad7f3c82fd62b694a97a435cb556f7))
* **client:** skip CurseForge file metadata flow for empty required files ([14de269](https://github.com/caunt/Void/commit/14de2691cd746481fb72496723761c97cba95471))
* **command:** 🐛 corrected command suggestion structure and plugin unloading behavior ([df3b9cf](https://github.com/caunt/Void/commit/df3b9cfedff4b3774a523ea23d5f51e66e9b0de5))
* **commands:** 🔧 fixed side bound interface ([3e15a72](https://github.com/caunt/Void/commit/3e15a72d5268a86b26e8bbd0b50d4d5f4ceb1b11))
* **console:** 🐛 corrected terminal input/output handling on proxy start/stop ([ee9e36d](https://github.com/caunt/Void/commit/ee9e36dec867fe3c7335248b440528e629d6ed39))
* correct workflow file extension from .yaml to .yml in runnable-pr.yml ([2f03081](https://github.com/caunt/Void/commit/2f03081a3aae7c997680c85712e411c028903208))
* **demo:** 🐛 install xxd and revert servers.dat generation to original command ([533d70a](https://github.com/caunt/Void/commit/533d70a2161bdfd4bf8da127789c77f4a3384c85))
* **demo:** 🐛 replaced xxd with python3 for hex-to-binary conversion in client Dockerfile ([ca9ddb0](https://github.com/caunt/Void/commit/ca9ddb084aeddd026475839fb84c2af3a8fe3b07))
* **dependencies:** remove retry loop from plugin container creation ([68415b6](https://github.com/caunt/Void/commit/68415b6531c46267f11dadd0890278290cce0b24))
* **dependencies:** use concurrent plugin container map ([9c04b8c](https://github.com/caunt/Void/commit/9c04b8c77ed3249705fd88b6359b94accb9cb5cd))
* **dependency:** 🐛 corrected instance factory creation and registry sharing behavior ([dfcd66c](https://github.com/caunt/Void/commit/dfcd66cd3327ac2bfe6bf6561a551acaf98a6151))
* **dependency:** 🐛 corrected plugin container retrieval logic in GetRootContainers method ([2dc31a0](https://github.com/caunt/Void/commit/2dc31a01f1910ce86fdb2c2e9f5aeea69fa5bb3d))
* **DependencyService:** 🐛 corrected container access methods for service resolution ([7e1537f](https://github.com/caunt/Void/commit/7e1537f3afd95544d079e10f6f66c794819f6fc4))
* **DependencyService:** 🐛 corrected player scope handling and improved service resolution logic ([cd383ee](https://github.com/caunt/Void/commit/cd383eefdaec923421c484ebac5c6b46e3295417))
* **DependencyService:** 🐛 corrected plugin container retrieval and improved player scope handling ([108cfbf](https://github.com/caunt/Void/commit/108cfbf3de8453f5bbb9abeb227569369a9a521c))
* **DependencyService:** 🐛 corrected resolver context handling for current scope ([7e2b347](https://github.com/caunt/Void/commit/7e2b34787036548ba2f6a633453b035648c439ab))
* **DependencyService:** 🐛 corrected service resolution logic by prioritizing service providers ([be88611](https://github.com/caunt/Void/commit/be88611b55c142587f69fb130b79f96a7d34de94))
* **DependencyService:** 🐛 corrected service resolution logic for singleton and dependency services ([47f22c8](https://github.com/caunt/Void/commit/47f22c894028e7c60ee175d52772f9d030096bed))
* **docs:** update workflow path filter from main.yaml to runnable-commit.yml ([a3627db](https://github.com/caunt/Void/commit/a3627db17472edc92e38cf2ddc76ab617b5055bf))
* **forwarding:** 🐛 corrected player profile null warning message and improved forwarding version logic ([108c2cc](https://github.com/caunt/Void/commit/108c2cc46a49772b30bfaabc6e26a58a3fd64cf0))
* **forwarding:** 🐛 removed debug break from phase change event handler ([2749516](https://github.com/caunt/Void/commit/2749516a5290f74363fbaa12ae2027227248c7a0))
* **forwarding:** 🐛 removed debug log from phase change event handler ([1c922c0](https://github.com/caunt/Void/commit/1c922c034dd207550158f324663e0cade9318e9d))
* **handshake:** 🐛 corrected casing for channel constants and moved PlayerCachedMods class ([eb8a086](https://github.com/caunt/Void/commit/eb8a086580f9124ab6a859ff94c50ddd41f00681))
* **HostingExtensions:** 🐛 corrected XML documentation formatting for clarity ([eef8dff](https://github.com/caunt/Void/commit/eef8dffcfcdaa8eb193bdf581f0e14c21b09072b))
* **IPluginService:** 🐛 corrected plugin loading logic and improved method signatures ([0bbd71b](https://github.com/caunt/Void/commit/0bbd71bf658bf3a76907c288522b9bd04b965dac))
* **link:** 🐛 corrected logging behavior for player connection ([37d5093](https://github.com/caunt/Void/commit/37d509371e21478680eb3836de1ebabe9f179a10))
* **link:** 🐛 corrected logging behavior for player connection ([d929e67](https://github.com/caunt/Void/commit/d929e6782cbe1fac2f810298465443e941c87732))
* **link:** 🐛 corrected player channel closure logic on link stop ([09ff668](https://github.com/caunt/Void/commit/09ff668266304420e0597dce40065fc8746ca6ae))
* **platform:** 🐛 resolved NuGet meta-package dependency assemblies ([a3582e3](https://github.com/caunt/Void/commit/a3582e3a86b208cf6c7194ecfe16bea7516335dc))
* **player:** 🐛 cleared player reference on disposal ([2eb3b3e](https://github.com/caunt/Void/commit/2eb3b3ee2dba6025c6d4df79908fbddb98954f3d))
* **player:** 🐛 corrected player type retrieval logic and improved null handling ([90dd96a](https://github.com/caunt/Void/commit/90dd96a39fa2440872dde47a7c69b746b7a599df))
* **players:** 🐛 switched kick link check to link service ([2d79d39](https://github.com/caunt/Void/commit/2d79d39a65a3b38c4eaf591ab9676517bbd063a1))
* **registry:** 🐛 corrected packet type name retrieval in PrintPackets method ([d5678fb](https://github.com/caunt/Void/commit/d5678fb9effd2bf3d5db3a143b44b090b01a9f60))
* update GitHub Actions workflow badge to reference correct workflow file ([cb6c52f](https://github.com/caunt/Void/commit/cb6c52fc1484515366baa639b4f849008245e3ee))
* use channel.SendPacketAsync instead of player.SendPacketAsync in KickPlayerAsync ([002326b](https://github.com/caunt/Void/commit/002326be6faffc5781aa621d98b72038d1df717b))
* **WeakPluginContainer:** 🐛 corrected plugin addition logic to ensure assembly consistency ([d5cf1e0](https://github.com/caunt/Void/commit/d5cf1e0145b49a30a90b9d5694eaed88b43b7c73))


### Performance Improvements

* **client:** 🐳 converted Dockerfile to multi-stage build to reduce image size ([f47d0c6](https://github.com/caunt/Void/commit/f47d0c6aeafdd24bce531e5df21d0647fa20cae7))


### Reverts

* **api:** ⏪ reverted src/Api and publish-docker.yml to main ([edd0fb3](https://github.com/caunt/Void/commit/edd0fb3300086221cc1872d47ed3c065f6199ec0))

## [0.5.16](https://github.com/caunt/Void/compare/v0.5.15...v0.5.16) (2026-05-19)


### Features

* **command:** ✨ added command suggestion functionality and improved command handling ([89d691b](https://github.com/caunt/Void/commit/89d691bee1f48f79a3ec3fa650f99f8e118c18be))


### Performance Improvements

* **trace:** 🔧 improved logging methods for message handling ([d0ee38e](https://github.com/caunt/Void/commit/d0ee38e8fdf35370978f43ad73e2eac65499ebd8))

## [0.5.15](https://github.com/caunt/Void/compare/v0.5.14...v0.5.15) (2026-05-18)


### Features

* **command:** 🔧 added CopyToAsync method for command node management ([8213a8c](https://github.com/caunt/Void/commit/8213a8c6c8eb00d4945467030fc57f1ff6f896ab))

## [0.5.14](https://github.com/caunt/Void/compare/v0.5.13...v0.5.14) (2026-05-18)


### Features

* **debug:** add paper 26 support ([e7ad678](https://github.com/caunt/Void/commit/e7ad6786f2142458cf754be3b99e764f74b4ab50))
* **logging:** ✨ enhanced console logging with a custom theme and improved argument parsing ([88d1983](https://github.com/caunt/Void/commit/88d198393a46f0a6da5aff514703d17b58c438b5))
* **protocol:** ✨ added RangeTo method to ProtocolVersion ([74ac790](https://github.com/caunt/Void/commit/74ac7903ae9c55b3287ffbf85c6f231b00cf509d))


### Bug Fixes

* **api:** 🐛 fix base class property search in ObjectExtensions ([ebe4c2c](https://github.com/caunt/Void/commit/ebe4c2ca063a6ee8e2e2a52c8a02f42301f640b0))
* **deps:** update dependency @astrojs/starlight to ^0.39.0 ([245f59b](https://github.com/caunt/Void/commit/245f59b77086a94a1c1873cd176435e28436a942))
* **deps:** update dependency starlight-links-validator to ^0.23.0 ([fac5ae7](https://github.com/caunt/Void/commit/fac5ae76cb831a6203d389101082e1d2835299c9))
* **deps:** update dependency starlight-links-validator to ^0.24.0 ([7894d0c](https://github.com/caunt/Void/commit/7894d0ceba0d79eccb5bd22e3b8a63965504595f))
* **events:** 🐛 handle potential `null` entries ([d7f0d46](https://github.com/caunt/Void/commit/d7f0d46f8508d82fb0a91069471db2efe93e0af5))
* **lifecycle:** 🐛 improve phase change handling and streamline keep-alive tracker logic ([7d4f93f](https://github.com/caunt/Void/commit/7d4f93fae530dc4502ec613239f2ee60de8ed65a))
* **logging:** 🐛 correct offline mode warning message ([3b16864](https://github.com/caunt/Void/commit/3b1686451a3cf63783cecb3fd9400b79f6142841))
* **network:** 🐛 enhance exception handling and buffer logic in `SimpleNetworkStream` ([96ff14d](https://github.com/caunt/Void/commit/96ff14dcd026f03a407b1a562507425a09118b4b))
* **registry:** 🐛 add trace logs for clearing transformation registries in player and server channels ([847519a](https://github.com/caunt/Void/commit/847519aee8f138a74af157458674c574a4295f81))
* **registry:** 🐛 fix async player iteration and ensure proper cancellation token usage ([0b16d33](https://github.com/caunt/Void/commit/0b16d33581c1b69c924e3dfe6f2551c8c620c227))
* **tests:** 🐛 adjust cancellation token usage, update log stability checks, and refine packet transformation handling ([4bae9e4](https://github.com/caunt/Void/commit/4bae9e4febbeedf96305b95ac2813f559e438681))
* **tests:** 🐛 renamed inconsistent fixture properties for clarity and stability ([7554aeb](https://github.com/caunt/Void/commit/7554aebea603e916991de1a2af4f0bc529138a41))

## [0.5.13](https://github.com/caunt/Void/compare/v0.5.12...v0.5.13) (2026-04-10)


### Features

* **api:** ✨ expose proxy interface and port properties ([67122a4](https://github.com/caunt/Void/commit/67122a4efe996cd3175d5dc91c6038f9a1c1bb81))
* **content:** ✨ sort releases by published date ([dd05667](https://github.com/caunt/Void/commit/dd056671237e6f900a02d78a9805ee464e56e730))
* **core:** ♻️ added async disposal for run results ([d46d76a](https://github.com/caunt/Void/commit/d46d76ae9586fbf67c8016012eee071564ed3af8))
* **events:** ✨ improve handling of player redirects ([0620a4e](https://github.com/caunt/Void/commit/0620a4eba31c0fc9a7ec6249772ac2225c6b9c89))
* **integration:** ✨ centralize display logic, improve scripts ([655981b](https://github.com/caunt/Void/commit/655981b90c22c7d02643f83955ca157727b2fc34))
* **integration:** ✨ improve container log and screenshot handling ([01e1db2](https://github.com/caunt/Void/commit/01e1db2155559e1724d846ce1f7097e8268353fc))
* **platform:** ✨ improve port detection logic ([357580d](https://github.com/caunt/Void/commit/357580d7747558d0efa3cedae6cdd202ace918af))
* **protocol:** ✨ added support metadata for Minecraft 26.1.2 ([90f8060](https://github.com/caunt/Void/commit/90f8060ac4970ed6706d5b22b3dd8d1b23424b18))
* **protocol:** ✨ implemented ReleaseVersion ([3ddc05d](https://github.com/caunt/Void/commit/3ddc05d9f1911130ecfbfe37d8be9844728ff3b0))


### Bug Fixes

* **ci:** 🐛 copy script files into docker build context and guard empty window id ([6873da2](https://github.com/caunt/Void/commit/6873da2241336ec50fc6361180cb2c4da18a61ae))
* **client:** 🐛 improve options reset and chat reliability ([2d4c5ec](https://github.com/caunt/Void/commit/2d4c5ecf9ee8dece19e91dea304c7a8124c0eadd))
* **core:** ✨ do not print lazy exit code property ([68e3656](https://github.com/caunt/Void/commit/68e365604a0d13b2e1b87c45643771f2a9377287))
* **deps:** update dependency starlight-links-validator to ^0.22.0 ([9a34a1c](https://github.com/caunt/Void/commit/9a34a1cfdae72c77eb92204b099aa08bb742ec53))
* **docker:** 🐳 preserve image layers for caching ([8eeaa7c](https://github.com/caunt/Void/commit/8eeaa7c16c1b419ff3d388683e7bbca8337c8e47))
* **platform:** 🐛 improve background task error handling ([d0688d4](https://github.com/caunt/Void/commit/d0688d47818c51d6e86befbc73bbcc6b493a71d9))
* **protocol:** ♻️ simplify version ordering logic ([31c2e03](https://github.com/caunt/Void/commit/31c2e03bac66daf23c09e67345804cc443afd993))
* **sitemap:** 🐛 prevent error if commit date missing ([e2d101b](https://github.com/caunt/Void/commit/e2d101bdd09093e33d884150319e50d0c301aaf9))
* **tests:** ⬇️ downgraded Microsoft.CodeAnalysis.CSharp from 5.3.0 to 4.14.0 ([0ad3ce4](https://github.com/caunt/Void/commit/0ad3ce432293cde6e249f8476f78a109db3a330e))
* **voidentrypoint:** ✨ return run result with completion task ([441ad40](https://github.com/caunt/Void/commit/441ad408c2b3219b6765692edeb26bd0e552a551))

## [0.5.12](https://github.com/caunt/Void/compare/v0.5.11...v0.5.12) (2026-04-04)


### Features

* **protocol:** ✨ added support for Minecraft 26.1 and 26.1.1 ([08104bf](https://github.com/caunt/Void/commit/08104bff9847c03e5d1d864bfa3e3c370a63a0f0))
* **tests:** 🎨 add support for testing multiple protocol versions in `PortableMinecraftClient` ([3c1364c](https://github.com/caunt/Void/commit/3c1364c7969de925ff3c06e3778914e2c417c076))


### Bug Fixes

* **api:** 📝 corrected player name fallback wording ([f2a985a](https://github.com/caunt/Void/commit/f2a985ab81adea744835b80a9289a16dd3810fda))
* **dependencies:** 🐛 corrected NuGet logging option casing ([3898183](https://github.com/caunt/Void/commit/389818317768790c77f861c810dfd1293cdb2b05))
* **deps:** update astro monorepo ([dd5944f](https://github.com/caunt/Void/commit/dd5944f9aff5269853ed342216395359cf448fbf))
* **deps:** update dependency @astrojs/starlight to ^0.38.0 ([98577a9](https://github.com/caunt/Void/commit/98577a912ee3b501271ee67f402088db800a7d1f))
* **deps:** update dependency astro to v6 ([cfa603a](https://github.com/caunt/Void/commit/cfa603ac2c16d2b66bee19d8013248394403d93a))
* **deps:** update dependency astro to v6 ([7f8b6ca](https://github.com/caunt/Void/commit/7f8b6ca4ccda2e924bc891810f016eaec6cd8b43))
* **deps:** update dependency sharp to ^0.34.0 ([66fb4c5](https://github.com/caunt/Void/commit/66fb4c589d86870942730008418b83c9ed61aec4))
* **deps:** update dependency starlight-links-validator to ^0.21.0 ([b07517b](https://github.com/caunt/Void/commit/b07517b04cbbce8cc0cef7ded7572cf3e6a13f82))
* **essentials:** 🐛 corrected override redirection typo ([34350b9](https://github.com/caunt/Void/commit/34350b9ce7387807d8af5a68cf4b68c19fffb3b6))
* **extensions:** ✨ improved input arrays parsing ([d803d90](https://github.com/caunt/Void/commit/d803d905ea848bbeff270983a5eea8cf46eb6d66))
* **link:** 🐛 improved handshake error handling ([b406702](https://github.com/caunt/Void/commit/b406702b229ae59dddee25a66acfdb61df28d213))
* **logging:** 🐛 update server args and improve output ([32fbf61](https://github.com/caunt/Void/commit/32fbf6187c5f9722bdaa2f82356be058e0014d0b))
* **minecraft:** 🐛 corrected NbtFile XML documentation typos ([1af9811](https://github.com/caunt/Void/commit/1af981115971c138f51cc8c5b77a65e581f20e1e))
* **minecraft:** 🐛 corrected TagType XML remark typo ([c8f17f5](https://github.com/caunt/Void/commit/c8f17f553bce99777f0d4cb0079fdccb54f861cb))
* **minecraft:** 🐛 updated `ReadComponent` to default `readName` to `false` for consistency ([6694f94](https://github.com/caunt/Void/commit/6694f949c43ac98f1c635c2a5bc44327c997e17f))
* **mods-support:** 🐛 corrected handshake error message grammar ([53c3b90](https://github.com/caunt/Void/commit/53c3b90f434b368452df7ae2f85ea3c9668d7aaa))
* **nbt:** 🐛 corrected langword XML doc typos in TagReader ([9af7969](https://github.com/caunt/Void/commit/9af7969a7e651b6a849f9d122e0b2d9ad2e213bf))
* **nbt:** 🐛 corrected typo in FormatOptions remarks ([7f383a0](https://github.com/caunt/Void/commit/7f383a06f79dd784994d11a88d8e5c8b294ac27b))
* **nbt:** 🐛 default `readName` flag to `true` across Nbt APIs ([e24ee13](https://github.com/caunt/Void/commit/e24ee13bcba5c6dd8f9e074f1f8e683d2fd32f4e))
* **nbt:** 🩹 corrected typo in TagReader XML comment ([b5595a2](https://github.com/caunt/Void/commit/b5595a2b39eeda6828144d0761cdc81e448b2c4e))
* **nuget:** 🐛 removed Void package exclusion logic ([82be530](https://github.com/caunt/Void/commit/82be530ebd48e9075fab5120dd960a1be0304e11))
* **platform:** 🐛 ignore early disconnects when kicking players ([9c7b54d](https://github.com/caunt/Void/commit/9c7b54d8c47294a32246df46ae23bd54ab42171c))
* **platform:** 🩹 corrected MIME type typos in resources metadata ([213bb94](https://github.com/caunt/Void/commit/213bb94bb559d045a379c51b8846d001989fad17))
* **registries:** 🛡️ added thread safety to packet id plugins registry ([07eb228](https://github.com/caunt/Void/commit/07eb228f79d6b2c70c31c29bf7e516fb9473a102))
* **tests:** 🐛 add platform-specific base images in `PortableMinecraftClient` Dockerfile ([15c6cdc](https://github.com/caunt/Void/commit/15c6cdcf9bdd79e9e0f07d997ca912e5846ecd0e))
* **tests:** 🐛 added environment check to DisposeAsync in integration tests ([e588aa7](https://github.com/caunt/Void/commit/e588aa724cbc79ed98f7b36898904b59ca4cf18e))
* **tests:** 🐛 fix --jvm-args argparse failure by using = syntax ([b1b132f](https://github.com/caunt/Void/commit/b1b132f85a75cb580e6f389ab48c0d8477a0f8e0))
* **tests:** 🐛 fixed image build platform and switched to System.Threading.Lock ([63b1bd5](https://github.com/caunt/Void/commit/63b1bd5b871fb2df7c10fd710119f93ac917d73c))
* **tests:** 🐛 fixed thread-safety, UTF-8 decoding and pipe completion in LogConsumer ([77e1d06](https://github.com/caunt/Void/commit/77e1d0607bf074f3214b2a5f23ddda7a1df3afcc))
* **tests:** 🐛 fixed unsynchronized Logs/ClearLogs access and silent exception swallowing ([06d5558](https://github.com/caunt/Void/commit/06d555814dc7d1196f20478fbca2ba489404086f))
* **tests:** 🐛 improved `PortableMinecraftClient` Docker setup and test timeout ([59e1721](https://github.com/caunt/Void/commit/59e17213f2ad81e37c26897e33845b6ce20cf8fe))
* **tests:** 🐛 improved Dockerfile setup and streamlined `RemoveImageAsync` process ([65be435](https://github.com/caunt/Void/commit/65be435c04fb76796c1b55737eb7c1261d0ef93d))
* **tests:** 🐛 monkey-patch portablemc to emit --quickPlayPath for MC 1.21 auto-connect ([8ba7dad](https://github.com/caunt/Void/commit/8ba7dad74eba6a3832096195b9447679205e76e7))
* **tests:** 🐛 refactored PortableMinecraftClient to use EndPoint and improved Docker argument handling ([d5499eb](https://github.com/caunt/Void/commit/d5499eb21a8fd0627cbedfb92945cfd058f45025))
* **tests:** 🐛 removed redundant debug logging in `IntegrationSideBase` ([6b1b297](https://github.com/caunt/Void/commit/6b1b297072351635b28b484507b2181ef7b66d5c))
* **tests:** 🐛 simplify Dockerfile base image definitions in `PortableMinecraftClient` ([5d5f7cd](https://github.com/caunt/Void/commit/5d5f7cdd271f175900799bdb28df91fe42ccb564))
* **tests:** 🐛 standardized timeout variable names in integration tests ([be1da5f](https://github.com/caunt/Void/commit/be1da5f9236a9238ff1bf8306c42d312fb25c920))
* **tests:** 🐛 track log timestamps and wait for silence in `PortableMinecraftClient` ([6834a3e](https://github.com/caunt/Void/commit/6834a3e055cd9dc76112c1a4a4b3f63e11642077))
* **tests:** 🐛 updated integration tests for consistency and removed unused attributes ([046194c](https://github.com/caunt/Void/commit/046194c17ea99365a9ba4f90287abf842aa3ca3f))
* **tests:** 🔧 add --quickPlayPath game arg and fix JRE CA cert import ([708d599](https://github.com/caunt/Void/commit/708d59916a5535b7d87bb36c6c41396bf33a751a))
* **tests:** 🔧 fix Python portablemc CLI args and remove pre-bake dry-run step ([3866eea](https://github.com/caunt/Void/commit/3866eea060dc2b056258c3e42b95f725eedd741c))
* **tests:** 🔧 import system CA certs into JRE truststore to fix SSL on proxy-intercepted environments ([caa62a3](https://github.com/caunt/Void/commit/caa62a388c0dbfac15368b3bb5245916542b92ee))
* **tests:** 🔧 move client start and connection wait into fixture setup to fix cold-run timeout ([319e8fa](https://github.com/caunt/Void/commit/319e8fa9ac94d966c8592abb26d736654f08964d))
* **tests:** 🔧 replace cargo install with GitHub release download for portablemc ([d88ffde](https://github.com/caunt/Void/commit/d88ffde92bf790850c866dd49108e02092a3a173))
* **tests:** 🔧 replace portablemc with node-minecraft-protocol to eliminate asset download bottleneck ([8534b9b](https://github.com/caunt/Void/commit/8534b9b6d74195bd85f717f3b4f8f4c4b5e622e8))
* **tests:** 🔧 restore portablemc client and fix Docker SSL trust for mkcert CA ([15a16a2](https://github.com/caunt/Void/commit/15a16a2b3d80870c48123d7e0944ae5213709ac6))
* **tests:** 🔧 use PaperPlugins.None for real client test to avoid Via plugin downloads ([bfcd609](https://github.com/caunt/Void/commit/bfcd60925814682b6ce2e66aec419b112b56bf37))
* **tests:** 🔧 use Python portablemc via pip and pre-bake MC download in image ([bf942fe](https://github.com/caunt/Void/commit/bf942fe764d0a0a1482e4052323886425f16460d))


### Reverts

* **tests:** ⏪ restore original Rust portablemc-cli Docker approach ([f789cde](https://github.com/caunt/Void/commit/f789cde8fba75f28de74f0f030c43c034d28c90e))

## [0.5.11](https://github.com/caunt/Void/compare/v0.5.10...v0.5.11) (2026-02-08)


### Features

* **di:** ✨ support context-aware service resolution ([0af0e9b](https://github.com/caunt/Void/commit/0af0e9b6bdf9cfd7763bb5b9d84583ff974dacf1))
* **nuget:** ✨ added option to enable NuGet logging ([387aa7b](https://github.com/caunt/Void/commit/387aa7bf11f1d90e420bbf2248f8cd41b5b22611))


### Bug Fixes

* **di:** ✨ activate dependency services later, after registration is complete ([a6eb07e](https://github.com/caunt/Void/commit/a6eb07eee33518ceaf5e5d81044f2f5a63a7be75))
* **di:** ✨ skip open generic service registrations ([e332ee0](https://github.com/caunt/Void/commit/e332ee01171f77ac08b3c3b749f0963aef6bfb10))
* **di:** 🐛 skip open generic types during registration ([864583e](https://github.com/caunt/Void/commit/864583e318ed7cba576feb753b7471d970cfb89e))

## [0.5.10](https://github.com/caunt/Void/compare/v0.5.9...v0.5.10) (2026-02-06)


### Features

* **commands:** ✨ improved argument type safety ([1edbc80](https://github.com/caunt/Void/commit/1edbc80927f3e5913f76bb2fd3693a124a3c93c0))
* **plugins:** ✨ detect service dependency relationships ([ac41cde](https://github.com/caunt/Void/commit/ac41cdeb1c967adb9acf804fb8cc5d23fdaf3974))


### Bug Fixes

* **di:** 🐛 warn and disable eager activation for dependencies ([857f6c7](https://github.com/caunt/Void/commit/857f6c75ae96d347dcd053c3d35168a1bb371a97))
* **logging:** 🐛 lowered log level for environment plugins loading ([72072d8](https://github.com/caunt/Void/commit/72072d8330ca95ca917d183505b59aefb96ca7bd))

## [0.5.9](https://github.com/caunt/Void/compare/v0.5.8...v0.5.9) (2026-02-05)


### Features

* **brigadier:** ✨ added support for mod and registry key args ([071bb9a](https://github.com/caunt/Void/commit/071bb9a83aa1075e81ac826e97c6420ae83a7eb5))
* **buffer:** ✨ added buffer copying and scoped write ([b46878c](https://github.com/caunt/Void/commit/b46878cce483f72645b69d944d9b128a1e2cedc9))
* **buffer:** ✨ added support for byte array read/write ([b17bddf](https://github.com/caunt/Void/commit/b17bddf9e0d3bea7a641f0c491d66382687457e6))
* **buffer:** ✨ added support for int array read/write ([3bff132](https://github.com/caunt/Void/commit/3bff13254a8d973db0849afec1dbf66bac751329))
* **buffer:** ✨ added VarInt array support ([c649278](https://github.com/caunt/Void/commit/c6492789b1e587c1580f23ec04d2f575242668c4))
* **buffer:** ✨ allow copying with reading buffer data to the end ([f43281c](https://github.com/caunt/Void/commit/f43281c27944e1333ef00df2f5374fe66449b937))
* **buffers:** ✨ enforce full buffer consumption on dispose ([7613b0b](https://github.com/caunt/Void/commit/7613b0bf4cbfc7ca8b809cfe9dd825afbc9ee46a))
* **commands:** ♻️ added argument parser definitions ([5d1781e](https://github.com/caunt/Void/commit/5d1781e3db1ef8f5669f19d049b266d90e556361))
* **commands:** ✨ added argument property serializers ([dabcdcb](https://github.com/caunt/Void/commit/dabcdcbab373dfe5007ccae9c735b847cc6058e0))
* **commands:** ✨ added bool argument serializer ([36649f7](https://github.com/caunt/Void/commit/36649f70161eb9cf2a8a8ed032cb2105fec086cc))
* **commands:** ✨ added packet / event support for available commands message ([5099a01](https://github.com/caunt/Void/commit/5099a016dba8bb9ef276942c99966e3fa58e405d))
* **commands:** ✨ added registry key arg serializers ([47bfd55](https://github.com/caunt/Void/commit/47bfd5576832b74eb9ba16f8570d7190bce63957))
* **commands:** ✨ moved cross stitch argument type support ([e66a869](https://github.com/caunt/Void/commit/e66a869a255aea584efd96b7819a6beaefb1f8f0))
* **commands:** ✨ remade brigadier arguments system to be non-generic ([5c108ff](https://github.com/caunt/Void/commit/5c108ff23c7339e8d9edb8617603e7af93575d04))
* **demo:** ⏱️ increased default session TTL to 2 hours ([10bd696](https://github.com/caunt/Void/commit/10bd6967275a27535e4d97d4f8ad74fe310c9a79))
* **demo:** ♻️ refactored to use shared itzg server across all sessions ([83c8c7b](https://github.com/caunt/Void/commit/83c8c7bfea8609bd1caa9ef3b0f79e6b29a5cd12))
* **demo:** ✨ added error handling to dashboard entrypoint ([45b4d6f](https://github.com/caunt/Void/commit/45b4d6f384ea034ad7ad287c230e99d866738676))
* **demo:** ✨ added multi-container dashboard demo ([5244859](https://github.com/caunt/Void/commit/52448596c2e10a270d02b6899215784d8a0f796d))
* **demo:** ✨ added REDIRECT_LOGS environment variable support ([89313d2](https://github.com/caunt/Void/commit/89313d2604820e65b70fef6bd7dd20d92278d99b))
* **demo:** 📊 enhanced logging for debugging session startup issues ([63f5f98](https://github.com/caunt/Void/commit/63f5f98d8683daa44c398c5db9a620d2f2daed5e))
* **demo:** added session management ([47cc5b1](https://github.com/caunt/Void/commit/47cc5b1dc4025e90bab86591a001a10000903281))
* **essentials:** ✨ added hostname-based server overrides ([be0a78d](https://github.com/caunt/Void/commit/be0a78d7d33447cf9f918bf5f98626f7f61b5dbc))
* **events:** ✨ added server address to handshake events ([955f65a](https://github.com/caunt/Void/commit/955f65aac01cd341069d2a3bd97255abe74a9b42))
* **events:** ✨ support player redirect in join event ([a7cc409](https://github.com/caunt/Void/commit/a7cc4095fe87fe5110092f2c7a9446382794e603))
* **logging:** 🔇 changed logging levels to debug for plugin and listener startup messages ([c0a6155](https://github.com/caunt/Void/commit/c0a61558425afc9b3e02c49e20bc7798f23df46b))
* **network:** ✨ added argument identifier mapping ([fb61421](https://github.com/caunt/Void/commit/fb61421ae820283c8d291173f0b01dcea97aebcd))
* **network:** ✨ updated identifier type and interface implementation ([c225072](https://github.com/caunt/Void/commit/c22507265afc7a7c8a67f408f943de6466fab583))
* **nuget:** ✨ added update checking for offline assemblies ([850231e](https://github.com/caunt/Void/commit/850231e1ac40067ac3d73d5fe449e2380a515557))
* **plugin:** ✨ added ProxyCompatibleForge plugin ([7b24e31](https://github.com/caunt/Void/commit/7b24e3178e58a1f8261b20bbb0d95f1ef060dee7))
* **plugins:** ✨ added CrossStitch support plugin ([7efbc6b](https://github.com/caunt/Void/commit/7efbc6b6fed20cf5cf8517bcd386f3a2c01af6cc))
* **protocol:** ✨ added ClientboundCommands packet mapping ([2d280a4](https://github.com/caunt/Void/commit/2d280a4bbd082c769ff93a6f389056cffaf0e024))
* **proxy:** ✨ added support for CommandsPacket handling ([e002094](https://github.com/caunt/Void/commit/e0020944181ed21cbc4326dcd8c15a629e47bc07))
* **proxy:** ✨ implemented chat command node graph structure serialization ([47402ff](https://github.com/caunt/Void/commit/47402ff1e438c50674afe25d6adfe74b5389a2e4))
* **server:** ✨ support per-server hostname overrides in file configuration ([36eef85](https://github.com/caunt/Void/commit/36eef85936e3d6366242feb84b87792487828c8e))
* **servers:** ✨ added server lookup by name methods ([845d0da](https://github.com/caunt/Void/commit/845d0da7c7e42700e11630114d699b5a08f64add))


### Bug Fixes

* **buffer:** 🐛 use DumpBytes for BufferSpan creation ([a77e427](https://github.com/caunt/Void/commit/a77e427a0552f93c5902a8dafca58d7fd3f305d5))
* **buffer:** 🛡️ enforced span lifetime safety ([f4a0872](https://github.com/caunt/Void/commit/f4a087283b10cc8860faa35d3a1a36c5ebce2e8c))
* **buffer:** 🛡️ improved int array reading safety ([eac39db](https://github.com/caunt/Void/commit/eac39dbc42a5922299d7afd51f720832e02365be))
* **demo:** ✨ added more optimization default options ([eaa342a](https://github.com/caunt/Void/commit/eaa342ad4952daef10a1b47c646828b121915400))
* **demo:** 🐛 added health check before connecting dashboard to network ([be214f3](https://github.com/caunt/Void/commit/be214f36689e9379906fcd916fd802e6c49f2fb9))
* **demo:** 🐛 corrected "still not ready" log spam prevention logic ([5ebdd91](https://github.com/caunt/Void/commit/5ebdd91af95dbd34eafd914373cd0e19bea7dc8d))
* **demo:** 🐛 corrected nginx environment variable syntax ([b9f7365](https://github.com/caunt/Void/commit/b9f7365375da109518f1e517c8dfaf62680c49c5))
* **demo:** 🐛 deferred DNS resolution for client container in nginx ([751b2e7](https://github.com/caunt/Void/commit/751b2e7ebbec08a5b22f17bddf6b880cbe45fde8))
* **demo:** 🐛 fixed dashboard container startup race condition ([120774a](https://github.com/caunt/Void/commit/120774ae02139e60ac365e27089d192427b8a140))
* **demo:** 🐛 fixed nginx hostname resolution for void proxy ([c8e06f4](https://github.com/caunt/Void/commit/c8e06f4bcc61ec37d6ff6bb92603864f14183101))
* **demo:** 🐛 prevent network leak on session startup failure ([6513c83](https://github.com/caunt/Void/commit/6513c8378c45796911e64e1942af268126976b0b))
* **demo:** 🐛 removed --rm flag to enable log retrieval on container failure ([dcb8051](https://github.com/caunt/Void/commit/dcb8051d30fadbd2e5a2a101a0358520a130ea53))
* **demo:** 🐛 updated page title to 'Void Demo' for clarity ([765b6d6](https://github.com/caunt/Void/commit/765b6d6de6335cdd900eae395abdab09b41bcaec))
* **demo:** 🐛 updated portablemc arguments for server joining and adjusted display resolution ([97c6a15](https://github.com/caunt/Void/commit/97c6a157bfbe869bfad606d12faa18aa84a614dc))
* **demo:** 🐧 build all images for linux/amd64 platform ([8622383](https://github.com/caunt/Void/commit/8622383bc097c59deadf0990e8db3d8dee6f204a))
* **demo:** 📃 survive page refreshes ([72ffda5](https://github.com/caunt/Void/commit/72ffda590a90a6cd578a32af8d3aa18d10f75ee5))
* **demo:** 🔧 made void ttyd writeable ([d7f32b9](https://github.com/caunt/Void/commit/d7f32b902d659e49735cca3164b65718270f96e9))
* **demo:** 🪟 fix Windows line ending issue in start.sh ([4fd0250](https://github.com/caunt/Void/commit/4fd025029e786bf6ead31c4302fb8dda03c22dd4))
* **deps:** 🐛 fixed PackageReader resource leak in NuGetDependencyResolver ([c65ca26](https://github.com/caunt/Void/commit/c65ca26d3ddfb878a440b94714aac6136a14ff13))
* **docker:** 🐛 enhance tmux session command to handle Ctrl+C and ensure continuous execution ([84a2789](https://github.com/caunt/Void/commit/84a27890e694775c6ff41df61db9a9abc9bc10cd))
* **docker:** 🐛 escape EOF in entrypoint script to prevent variable expansion ([26a07be](https://github.com/caunt/Void/commit/26a07befe944c03b639735a06ffa57e76d1876e8))
* **docker:** 🐛 removed 'latest' tag from ttyd image for consistency & tmux'ed itzg container ([8528edd](https://github.com/caunt/Void/commit/8528edd694707bfe51385c425eaf76b4daed337f))
* **forge:** 🛠️ include server address in handshake result ([6e27897](https://github.com/caunt/Void/commit/6e278979d9cf14b5ee096c7a316dd946e32fdc65))
* **forwarding:** 🐛 improved empty key disconnection message ([79999be](https://github.com/caunt/Void/commit/79999be1d97b084e26f01591ef2b3db4cc9e7488))
* **lifecycle:** 🐛 ignore StreamClosedException on keepalive ([5e027d3](https://github.com/caunt/Void/commit/5e027d32c9372d793dd9ece46753638740672d1c))
* **logging:** 🐛 clarify Velocity forwarding key message ([3bc9161](https://github.com/caunt/Void/commit/3bc9161320adb64b4640f214dc8aa4a082341add))
* **logging:** 🐛 improved CommandsPacket logging output ([aa78fa9](https://github.com/caunt/Void/commit/aa78fa921bc34efe4731868a71bfd2514c4fdd68))
* **logging:** 🐛 improved repository option docs and logging ([0be1d06](https://github.com/caunt/Void/commit/0be1d06c009ef3c5cf380fb3890fb15c9bd499b9))
* **logging:** 🐛 log disconnect reason without legacy format ([cc68514](https://github.com/caunt/Void/commit/cc685149c081b9d3ccae43f58485517c3a143ad8))
* **mods:** ✨ share header size mapping across instances ([1bc94c9](https://github.com/caunt/Void/commit/1bc94c92c636725165c71dae780e325846d153e3))
* **modssupport:** 🛑 signal unsupported parsing ([9f7ef9a](https://github.com/caunt/Void/commit/9f7ef9a7d34388249f4431b25f8b819a4bc1101b))
* **nuget:** ♻️ addressed code review feedback ([47ff527](https://github.com/caunt/Void/commit/47ff527cf7fc978d6f105f2162e5db0c95aa610d))
* **nuget:** ♻️ improved logging and cancellation token handling ([9e27baf](https://github.com/caunt/Void/commit/9e27baf12a1237e33be66638b5aaab0b6d0a5d49))
* **nuget:** 🐛 allow major version updates during automatic update checks ([c755b34](https://github.com/caunt/Void/commit/c755b34e3f5fd0402bea45c5d719f611959d2158))
* **nuget:** 🐛 improved error message accuracy for assembly resolution ([97fbf87](https://github.com/caunt/Void/commit/97fbf87c1663c6d7b9403be1d0c783b3b278e745))
* **nuget:** 🐛 improved update download validation and package ID resolution ([fa72f24](https://github.com/caunt/Void/commit/fa72f24c96b89bfd6355adb8cbad7b1a714699f2))
* **nuget:** 🐛 improved version compatibility and assembly fallback logging ([934b46f](https://github.com/caunt/Void/commit/934b46f13b0a6759590183913fb735d33a29cbb0))
* **nuget:** 🐛 prevented deadlocks by adding ConfigureAwait(false) to all async calls ([f14d530](https://github.com/caunt/Void/commit/f14d530f6faa9ec0ff0f96bf4d8ddbff95495804))
* **serializer:** 🐛 updated constructor param to match casing ([2589fe8](https://github.com/caunt/Void/commit/2589fe8883e3f5fdcfbbac4943bc2cce1415f80d))
* **session:** 🐛 skip probing for empty host strings ([379c71e](https://github.com/caunt/Void/commit/379c71ed2f37ceb1f81294f39642d21031adbfd5))


### Performance Improvements

* **events:** ✨ slightly optimized event re-invocation selection ([58cabd1](https://github.com/caunt/Void/commit/58cabd1fae85869f13399f8352632ec03278fd90))

## [0.5.8](https://github.com/caunt/Void/compare/v0.5.7...v0.5.8) (2026-01-28)


### Bug Fixes

* **di:** ✨ skip activation of open generic services ([82603b8](https://github.com/caunt/Void/commit/82603b85191bf3582f03617a6376ae0179ab769b))

## [0.5.7](https://github.com/caunt/Void/compare/v0.5.6...v0.5.7) (2026-01-27)


### Features

* **auth:** ✨ handle early disconnects for status queries ([37a1839](https://github.com/caunt/Void/commit/37a183991b4c0c11d9873939f2ca2468dea1d5ca))
* **link:** ✨ improved player reconnection on failed connection requests ([cacd51a](https://github.com/caunt/Void/commit/cacd51a9cc55104dd7c2ddd8aa4281871b5c7ca6))
* **nuget:** ✨ added NuGet repository probing functionality ([c355700](https://github.com/caunt/Void/commit/c3557004a1cac191fbacc5876292998a2c5e77d9))
* **protocol:** ✨ expanded SetHeldItem packet definitions from 1.7.2 to latest ([9e9f2b4](https://github.com/caunt/Void/commit/9e9f2b4857fa1bc23095f57ecfddcec6be2ddc8a))
* **resolver:** ✨ improved assembly path resolution ([a2b0bed](https://github.com/caunt/Void/commit/a2b0bed59b8e576d5bff67e913e1fa7331632b8c))
* **server:** ✨ allow --server option without port, default to 25565 ([0eb44d9](https://github.com/caunt/Void/commit/0eb44d901f000a7763774df6bd77effc43f85238))
* **server:** allow --server option without port, default to 25565 ([8c6cc88](https://github.com/caunt/Void/commit/8c6cc8885693269279d92456baf84ef40945bcd1))


### Bug Fixes

* **common:** 🐛 corrected cancellation log spelling ([5612838](https://github.com/caunt/Void/commit/56128383c68f208eba805106a8e3ffb8561af30b))
* **essentials:** 🐛 corrected log message grammar ([742aa15](https://github.com/caunt/Void/commit/742aa1571bc3376e735b6c1703e5a3b493f90f3f))
* **keepalive:** 🐛 reset tracker on server redirection ([6d73acd](https://github.com/caunt/Void/commit/6d73acdc7e5662debb6c6915d6437d3f025da460))
* **kicks:** ✨ allowed changing phase to login without a link to kick players early ([b8df337](https://github.com/caunt/Void/commit/b8df3371ae36f943d12e9c2fafc995dbe1f41499))
* **lifecycle:** ✅ added IsMinecraft check in OnPlayerDisconnected ([6fecca8](https://github.com/caunt/Void/commit/6fecca8a71661a5398816e4247bf7fbe7253e0fd))
* **links:** 🐛 allow multi-server retry on connection failure ([7f13515](https://github.com/caunt/Void/commit/7f13515c95657ca94a921b46f04d1f0c93d9db99))
* **links:** 🐛 corrected timeout log wording ([5e2a687](https://github.com/caunt/Void/commit/5e2a6873e54a3312eebdd7c881095100b6c69e8d))
* **minecraft:** 🐛 correct XML doc langword typo ([0d40cf8](https://github.com/caunt/Void/commit/0d40cf84cc79c6884c0bdad6be1ce8c51d79b1fa))
* **nbt:** 🐛 corrected network format description ([6ec23d3](https://github.com/caunt/Void/commit/6ec23d3918b6e99969a503c38ec9e705d10e256c))
* **nuget:** ✨ improved custom repository handling and error logging ([61a70c5](https://github.com/caunt/Void/commit/61a70c50a900ac4d71a8d55a0273bfc095184f09))
* **nuget:** 🐛 declare sanitizedUrl once and assign appropriately ([0b87567](https://github.com/caunt/Void/commit/0b87567c40939cb4b412869f7897d812bbb1230f))
* **nuget:** 🐛 initialize sanitizedUrl once, reassign only for valid URIs ([2147d98](https://github.com/caunt/Void/commit/2147d98baaa2ec95dcd55e2693b6444898667cac))
* **nuget:** 🐛 removed duplicate sanitizedUrl reassignment ([7d725fe](https://github.com/caunt/Void/commit/7d725fe37dee358ba999a1c4030facfa0d1d3c23))
* **nuget:** 🔐 keep credentials in HTTP probe requests ([280e8eb](https://github.com/caunt/Void/commit/280e8eb220f6310dd04561666ec1293e5d2f025d))
* **nuget:** 🔒 added sanitized URI to invalid repository logging ([72a47d0](https://github.com/caunt/Void/commit/72a47d014e5a7fa5a15472c6ff7a84e61f238529))
* **nuget:** log error status code instead of general failure ([4e622c3](https://github.com/caunt/Void/commit/4e622c3a42eac1f885ab5326aa60c0f804360b0a))
* **player-service:** 🐛 updated error messages for server connection failures ([0a6a5ef](https://github.com/caunt/Void/commit/0a6a5ef00ae4ba109cf59d0b45d53cd63bd0ca90))
* **player:** 🐛 unwrap player before kick operations ([1ce1b16](https://github.com/caunt/Void/commit/1ce1b16ab71973d5b9cf1888b03bb0b4c36f41c7))
* **plugins:** 🐛 honor remote plugin URLs ([525a456](https://github.com/caunt/Void/commit/525a4567c12cdd1b2d9c6e132d1a77be0aaf8904))
* **plugins:** 🔇 set HttpClient log level to Warning to suppress verbose logs ([2c62348](https://github.com/caunt/Void/commit/2c623486b26baf975e53c2c6ace94eefa0123bdd))
* **plugins:** 🔇 suppress verbose HttpClient logs and show full URI in errors ([9b4db14](https://github.com/caunt/Void/commit/9b4db146353429d17c68daa28bfdaf409db774be))
* **protocol:** 🐛 logged actual player instance instead of TcpClient ([9780a94](https://github.com/caunt/Void/commit/9780a94f5f40ff3ad6783c3fa2d5b711e898e541))
* **resolver:** 🛠️ handle missing plugins directory safely ([d8af77e](https://github.com/caunt/Void/commit/d8af77e431b4cb51ddbf1c01903c79ed40b8e7b2))


### Reverts

* remove last two rebase-related commits ([e317f8e](https://github.com/caunt/Void/commit/e317f8ed59020e7c3769e31be3488a4cb7fa81fb))

## [0.5.6](https://github.com/caunt/Void/compare/v0.5.5...v0.5.6) (2026-01-12)


### Features

* **api:** ✨ added player name property ([361cb9b](https://github.com/caunt/Void/commit/361cb9b08d0abdc860695f6d97999172c7c02b05))
* **buffer:** ✨ added text components json serialization support for network buffers ([0edb11f](https://github.com/caunt/Void/commit/0edb11fe8ed7f1a2899b126a7af342dcfad136dc))
* **buffer:** ✨ unified component (de)serialization API ([c840c21](https://github.com/caunt/Void/commit/c840c21e7cc01dc1d6b6a949f8b40d17870a1a21))
* **buffers:** ✨ added support for large buffer indices ([2a96fb5](https://github.com/caunt/Void/commit/2a96fb5da5ffa1841cc1187fc26a2a79929d8a6c))
* **components:** ✨ added more control over tag name serialization ([058f8e0](https://github.com/caunt/Void/commit/058f8e006b00906b9a42302d80cf61862249c5a1))
* **components:** ✨ added support for literal content type ([8518d28](https://github.com/caunt/Void/commit/8518d2888af1970a7673705e9fa44c4a81329cf6))
* **core:** ✨ migrate some APIs to extension properties ([51ee50d](https://github.com/caunt/Void/commit/51ee50d205026aea1040b7638a160a3f50d05d02))
* **events:** ✨ added PlayerJoinedServerEvent when both sides in Play phase ([4528dae](https://github.com/caunt/Void/commit/4528daeefeb571fd345e22bdb7c257e8e7297ec3))
* **extensions:** ✨ added flexible packet transformation API ([cd8cb6a](https://github.com/caunt/Void/commit/cd8cb6a2cdae08a87d49e3493703736fb48fe1f5))
* **lifecycle:** ✨ added keep alive tracking and timeouts ([1be6f6f](https://github.com/caunt/Void/commit/1be6f6f0e7adb091f9edadc205c4876b29c210b7))
* **nbt:** ✨ added TryGetValue method to NbtCompound ([bceed05](https://github.com/caunt/Void/commit/bceed05cead311c14fb8f0bee21e0eb47bf0d60b))
* **nbt:** ✨ integrated archived Eric Freed's project SharpNBT ([91356b3](https://github.com/caunt/Void/commit/91356b3210957ce9e56ffd7dcd5a4613d07898c7))
* **network:** ♻️ centralized packet transformation registration ([8ff8dcd](https://github.com/caunt/Void/commit/8ff8dcdbab9c29296bd0a78cb612ec172bae4d8b))
* **network:** ✨ centralized packet id definitions ([682db46](https://github.com/caunt/Void/commit/682db46c60377cca1531d4b830e829aa1d18ac7f))
* **network:** ✨ implemented stream timeouts ([ad0253f](https://github.com/caunt/Void/commit/ad0253f9047c1972994f03e4f39ddf0f14aa0a40))
* **properties:** ✨ added more parsing capabilities ([f1bd643](https://github.com/caunt/Void/commit/f1bd643eb9d38de3e8bb18f9ea7611619b411316))
* **properties:** ✨ improved JSON serialization flexibility ([48ad114](https://github.com/caunt/Void/commit/48ad1146d436d63781d79865d5174ab1347da382))
* **serializer:** ✨ improved NBT tag type safety ([8e77609](https://github.com/caunt/Void/commit/8e776093fcc40f44abdea00023f3f96f8041921e))


### Bug Fixes

* **api:** 🐛 improved error handling and logging ([5bf37e9](https://github.com/caunt/Void/commit/5bf37e98c764dc8abdf647b08980c49924c8d02f))
* **auth:** 🐛 improved error handling for early closing connections ([873550e](https://github.com/caunt/Void/commit/873550ea98e08e3e354de5d193252b63bb8ae45d))
* **buffer:** 🐛 changed default value of writeName parameter to false for components ([756d97e](https://github.com/caunt/Void/commit/756d97e35aca6c78973772969a02e87c90c47159))
* **buffers:** 🐛 standardize buffer exception parameter order ([989a9e1](https://github.com/caunt/Void/commit/989a9e15130b6a7b4ea849039801604f743a95a5))
* **buffers:** 🛑 improved end-of-buffer error throwing ([da0ec0e](https://github.com/caunt/Void/commit/da0ec0e9ccca0ad869925a317216ea1fa3f36ed2))
* **command:** 🐛 improved unknown command handling ([0512629](https://github.com/caunt/Void/commit/0512629c0993cbd2366c20ef63ff5cbed86ce289))
* **command:** 🐛 prevented deadlock by not awaiting task from itself ([af68f35](https://github.com/caunt/Void/commit/af68f35f00f600f116f41339ae5ccf058a169498))
* **commands:** 🐛 ignore StreamClosedException in execution ([11ca15d](https://github.com/caunt/Void/commit/11ca15d4cc399a2eb7214d83e3b6c8c42ebc259c))
* **components:** ♻️ updated components to latest vanilla implementation ([8683da5](https://github.com/caunt/Void/commit/8683da51561f8326392d1fc6c212f69c10fdbd3c))
* **dependency:** 🐛 use cached player hash for lookups ([c88a0ac](https://github.com/caunt/Void/commit/c88a0acd15d6d13bdc9319b07ab2e329be6ed6c6))
* **exceptions:** ✨ improve exception messages with better context ([a2d2d98](https://github.com/caunt/Void/commit/a2d2d985883c6f1ce273ba94b4d629fd5347e4e4))
* **lifecycle:** 🐛 improved keep-alive handling and logging ([44958dc](https://github.com/caunt/Void/commit/44958dc7439c4c42e1bf71e46712358d5dfdfa20))
* **links:** 🐛 improved error handling in authentication ([74f118f](https://github.com/caunt/Void/commit/74f118fa14e8eb77800df7cc8f56d63c7796dc40))
* **logging:** 🐛 improved error handling for command executions ([aadae16](https://github.com/caunt/Void/commit/aadae1643357954b5acd817cff2489161cb85c19))
* **minecraft:** ✏️ correct tag parsing comment typo ([9552d8b](https://github.com/caunt/Void/commit/9552d8b9d36d9054238024e53ee0ad008b51631c))
* **nbt:** 🐛 allowed empty tag names like vanilla does ([cdf2a88](https://github.com/caunt/Void/commit/cdf2a885fbc4cd0b7d949aed0f53d8f206454303))
* **nbt:** 🐛 use Fields property instead of Values ([533908b](https://github.com/caunt/Void/commit/533908b99bb5dfe98052a70c66a0448fdda0c6e0))
* **nbt:** 🧹 removed unsigned type support from public API ([ce8402f](https://github.com/caunt/Void/commit/ce8402f7538f47e35a062394657ba312eba13981))
* **network:** ♻️ unified disconnect packet definition ([faea054](https://github.com/caunt/Void/commit/faea054f799eaebd3ef45b2b60e4ec8961bd5755))
* **network:** 🐛 reduced mods logging ([c2f235c](https://github.com/caunt/Void/commit/c2f235c7f11c84b2c9b47f685ef5194461774ba0))
* **network:** 🛠️ improved packet decoder method lookup ([17c788b](https://github.com/caunt/Void/commit/17c788b980e2bfa86202415dd7842002d1825fc5))
* **platform:** ♻️ renamed entry point for clarity ([b281541](https://github.com/caunt/Void/commit/b281541de1afdf539332753fa1dd71af8e1d70ae))
* **platform:** ✏️ correct suppression comment ([3a8d387](https://github.com/caunt/Void/commit/3a8d387c896d289df32b6c943303806650b8d4cd))
* **player:** 🐛 improved disconnect handling and logging ([6043c95](https://github.com/caunt/Void/commit/6043c95919dfbeb45b716628ccd1c7e725d23e03))
* **player:** 🐛 improved player disconnect event handling ([84c7db8](https://github.com/caunt/Void/commit/84c7db8e8fa25dd424f38b5f474c5a37ae866670))
* **player:** 🛠️ simplified player kick logic ([9c3887a](https://github.com/caunt/Void/commit/9c3887a5e32d2e43224269dc46fe7e0180eaf0c5))
* **protocol:** ✨ added keep-alive packet id mappings for all versions ([3d77afb](https://github.com/caunt/Void/commit/3d77afbeb6286fec81d5d3a1095a9e5d75c1d4ee))
* **protocolsupport:** ✏️ clarify login disconnect comment ([ceeafe8](https://github.com/caunt/Void/commit/ceeafe8909eceb166ec6007493a880d49c9698be))
* **protocolsupport:** 📝 correct authentication comment typo ([566b312](https://github.com/caunt/Void/commit/566b31286975b1a362b9be4f026dec172f0bb315))
* **registry:** 🛠️ skip empty packet transformation mappings ([87a434c](https://github.com/caunt/Void/commit/87a434ccc1c1ca6799604cdedf6864aa49bbaf13))
* **services:** 🛠️ improved error handling for disposed players context ([de974c8](https://github.com/caunt/Void/commit/de974c81b2cb5411302084b6b85faa99ad37d98e))
* **transformations:** 🛠️ register on server play phase only ([9bee10b](https://github.com/caunt/Void/commit/9bee10be6766afb9c96ff7268e2b7c2c8c807c39))


### Reverts

* **brigadier:** ⏪ revert exception improvements in CommandContext ([5d5536a](https://github.com/caunt/Void/commit/5d5536aeed0722ba42af2123e005cb9b7fab6e6e))

## [0.5.5](https://github.com/caunt/Void/compare/v0.5.4...v0.5.5) (2025-12-18)


### Features

* **api:** ✨ introduce run options interface and configuration ([0738af7](https://github.com/caunt/Void/commit/0738af778c9867378f5812cb579cd6dea1b00c7c))
* **auth:** ✨ improve authentication result feedback ([5ec8677](https://github.com/caunt/Void/commit/5ec8677d556bb08b74d8a8c39f8960d0e39b81fc))
* **authentication:** ✨ added login plugin message handling ([4cd1c2f](https://github.com/caunt/Void/commit/4cd1c2f96af0aafa59d41d99a460b6bd2d00732f))
* **buffer:** ✨ added more dump methods ([201bc51](https://github.com/caunt/Void/commit/201bc510c2b798b7d4c67b7e6f5a7793e637d2fc))
* **channels:** ✨ added Channels property to ILink to simplify enumeration over channels ([7e19e20](https://github.com/caunt/Void/commit/7e19e20f42bdc86f16917545c661bd2f9d14773f))
* **config:** ✏️ added read-only mode to disable saving changes to configuration files ([3018667](https://github.com/caunt/Void/commit/30186679f2b78b155b7aba74fd44232b85e883de))
* **console:** ✨ allow passing command-line options to plugins ([36944b5](https://github.com/caunt/Void/commit/36944b56738c6f7dc8b27b1f8dc4d1b10e5de8c7))
* **console:** ✨ manual command-line option discovery ([2e5124a](https://github.com/caunt/Void/commit/2e5124afa8367168f66d4e9c3c9ed22894db2579))
* **debug:** ✨ support forge servers in debug harness ([c28ceac](https://github.com/caunt/Void/commit/c28ceac1eaf907bf6fe083943b479a8b82cf7e47))
* **events:** ✨ added event waiting functionality ([edea1a9](https://github.com/caunt/Void/commit/edea1a9815ba029ee9e9053c0447908831db020d))
* **events:** ✨ added side to handshake event ([e61321b](https://github.com/caunt/Void/commit/e61321b88836e8bbd269629c7a54a25b6dfab76b))
* **forge:** ✨ added handling of forge handshake ([0661c26](https://github.com/caunt/Void/commit/0661c2653e7e1951718abef2f3d405a03a475372))
* **forge:** ✨ handle markers in handshake ([3bfee5d](https://github.com/caunt/Void/commit/3bfee5d573e3f03cae1ab29729fe6037686337ac))
* **forge:** ✨ listen to handshake packet ([b1e8d84](https://github.com/caunt/Void/commit/b1e8d84005db9a8a96934f8048d4e0b92e3ecd6d))
* **forge:** ✨ reuse cached client mods to allow fml servers redirections ([2d93c16](https://github.com/caunt/Void/commit/2d93c160af2be7a6803fbd203dd0c6e5b02ad9f3))
* **forwarding:** ✨ allow passing modern forwarding key by command-line option ([19257cc](https://github.com/caunt/Void/commit/19257cc55d0dc5908c02eb23d6977fa29e532ecd))
* **inventory:** ✨ add example support for 1.20/1.20.1 versions ([f93dc9c](https://github.com/caunt/Void/commit/f93dc9c9b04206864a0b3ed5fb24e804cacaa50a))
* **logging:** ✨ introduce dynamic logging level control ([53c6805](https://github.com/caunt/Void/commit/53c6805081629eac55df837b6e13aea5530e8df4))
* **logging:** 🔧 added logger to player scoped context ([45ee246](https://github.com/caunt/Void/commit/45ee2467b57f5ed9c816f1ec0b0d8f72f886a791))
* **options:** ✨ use working directory from run options ([4e40d25](https://github.com/caunt/Void/commit/4e40d254efafb1d605df31c206b957cdce61de22))
* **packets:** ✨ added play phase plugin message codec ([25c1d9e](https://github.com/caunt/Void/commit/25c1d9e29c467c4f7d0af4c50e61e2e16f0852b1))
* **player:** ✨ added ConnectedAt property ([c7268a8](https://github.com/caunt/Void/commit/c7268a89f98b48a52bdf6c2b34482a05482f1510))
* **players:** ✨ added traffic pause/resume api for players ([c8dc96f](https://github.com/caunt/Void/commit/c8dc96fb69fc16acbc32d55800a1482162b6b7f4))
* **protocol:** ✨ add support for 1.21.11 protocol version ([e0fddca](https://github.com/caunt/Void/commit/e0fddca736e2c2a4bc7e11e03dae3d5d0e20299e))
* **protocol:** ✨ add support for version 1.21.10 ([0ad76fd](https://github.com/caunt/Void/commit/0ad76fde472746d73bb557bf5064ed058ded98bc))
* **registries:** ✨ add direction-based clearing ([4cfc442](https://github.com/caunt/Void/commit/4cfc442e20f8a917d6448ca9d816e5370a1b6f71))
* **version:** ✨ support 1.21.9 minecraft ([7570d3a](https://github.com/caunt/Void/commit/7570d3a6e9d9e0534d529c9e55b8a491cd1bdf3d))


### Bug Fixes

* **auth:** 🐛 correct typo in server authentication comment ([18eb4e1](https://github.com/caunt/Void/commit/18eb4e1d983db2c93698b6072df795dc9e632b37))
* **auth:** 🐛 ensure non-null auth result default ([603453b](https://github.com/caunt/Void/commit/603453b5ea2b37d4400f8eb342034de98b15ccbf))
* **auth:** 📝 correct response wording ([bff47aa](https://github.com/caunt/Void/commit/bff47aa7383c2097ca78c8da552e4206e722d4b3))
* **auth:** 🛠️ only finalize server login on successful auth ([22e908c](https://github.com/caunt/Void/commit/22e908cda48879a8fe752850bdd9c1390d6227db))
* **auth:** 🛡️ finish login after login is completed on server ([502c319](https://github.com/caunt/Void/commit/502c319704cb54654efcbbc2e102f63b6aa750bd))
* **auth:** 🛡️ separate player and server auth results ([7ae7b2b](https://github.com/caunt/Void/commit/7ae7b2b20ec36ab83468b29153c067ffd9099c61))
* **authentication:** ✨ allow more types of packets at authentication ([5c2d07b](https://github.com/caunt/Void/commit/5c2d07b8eb2513465c0e8771f95c41518ee33766))
* **channels:** 🔧 throw channel created event after player context channel property is set ([048f474](https://github.com/caunt/Void/commit/048f474d59f0ef826561f2c7a28b1974059075ee))
* **chat:** 🔧 compare plugin with example plugin ([377e799](https://github.com/caunt/Void/commit/377e799b53af298e789984e3301dfb2883b3ca8b))
* **commands:** 🐛 improved command execution tracking ([3f3ce1f](https://github.com/caunt/Void/commit/3f3ce1f27183e5ff41e63b281d40ad66cca87175))
* **common:** 📝 correct channel registry comment ([bca0f9f](https://github.com/caunt/Void/commit/bca0f9f7e1e3d14b3956c2674ee439cafdbcd29c))
* **components:** 🐛 improve JSON deserialization logic ([2477dfe](https://github.com/caunt/Void/commit/2477dfeab3369c4600aadac020dad3e2499ae2c8))
* **config:** 🧹 ensure all plugin configurations are removed ([fdf5974](https://github.com/caunt/Void/commit/fdf5974c22b9e927874ae4221c3985723dbbe663))
* **console:** ♻️ remove setup method and update buffer for each read ([c9b4dd7](https://github.com/caunt/Void/commit/c9b4dd791fbfe4b64bcb8e7a0156c0f734aab587))
* **console:** ✨ check for options implicity ([0d42b15](https://github.com/caunt/Void/commit/0d42b155842ee76ae7f7298c8952bfecd6de1775))
* **console:** ✨ simplify command-line option detection ([969f0a1](https://github.com/caunt/Void/commit/969f0a1723fc66ecc35a7280e696c924f5807496))
* **console:** 🐛 remove unreachable null check ([12b5e7d](https://github.com/caunt/Void/commit/12b5e7d0b8db87c5c6a36d707c9bedecd122af66))
* **dependencies:** ✨ reimplement file dependency resolver to escape deps.json requirement ([6c96a28](https://github.com/caunt/Void/commit/6c96a2859ee1b100ecf5f7861dc5a8b785ac39bb))
* **dependencies:** 🛠️ improved assembly resolver path selection ([8f12339](https://github.com/caunt/Void/commit/8f123398799cadbd3da3dd17e47bb6eeaf1f65b3))
* **dependency-injection:** ✨ discover repository option early ([b6368b8](https://github.com/caunt/Void/commit/b6368b8c25bd5da09a2c18831050b57779a4f0ac))
* **di:** 🔧 use adapter for DryIoc DI rules ([80e67f3](https://github.com/caunt/Void/commit/80e67f36b69ff7d7b71de9b86fd9eceb46248ffe))
* **entry-point:** ♻️ optimize working directory handling ([f0c2c33](https://github.com/caunt/Void/commit/f0c2c33535a671f72b71c5df76a023bdc3667470))
* **entry-point:** 🐛 do not pass cancelled token ([b01225b](https://github.com/caunt/Void/commit/b01225bc99a8f0cf19c8dfe6192764f642ea699b))
* **essentials:** ✨ simplify server redirections ([78fd87b](https://github.com/caunt/Void/commit/78fd87b0a2c6e8750b4947222a410074a6efafaa))
* **events:** 🐛 enumerate all channels when rethrowing system packets ([0de8bbf](https://github.com/caunt/Void/commit/0de8bbf5a232b1a7f98b8b3a53793d32874c9da9))
* **example-plugin:** 📝 add missing article in comment ([235a79e](https://github.com/caunt/Void/commit/235a79ef56ca035938eb5bc1d709aac0a2cbfd1d))
* **example-plugin:** 📝 correct plugin load comment typos ([abc094b](https://github.com/caunt/Void/commit/abc094be164ff1a279c8bbd201cc006a31b0da1f))
* **forwarding:** 🔧 ensure forwarding option is discovered early ([4075c50](https://github.com/caunt/Void/commit/4075c50ff9d958ed9bdc41cce8d1594f14422e47))
* **forwarding:** 🔧 use selected secret key ([563b5ac](https://github.com/caunt/Void/commit/563b5acb7bc33dc8ef3bc62043aa80ea6f044ae1))
* **inventory:** 🛠️ update packet mappings to 1.21.9 ([c10af19](https://github.com/caunt/Void/commit/c10af1954d33bc66d50e57019fcf0b02f2b05694))
* **listener:** 🔧 change message processing order ([a1a57b1](https://github.com/caunt/Void/commit/a1a57b13520bed30e93cde1ea5c5d64d3d29d64f))
* **logging:** 🐛 improve error handling and logging ([7cc423c](https://github.com/caunt/Void/commit/7cc423c84070817930982dd3a7e5132212762ff7))
* **logging:** 🐛 improve log detail and direction enum usability ([2a29797](https://github.com/caunt/Void/commit/2a29797e27840c1455f10e53b8923d39f366c5a6))
* **logging:** 🐛 improve timeout error messages ([9dd5469](https://github.com/caunt/Void/commit/9dd546976b436eb8ea2a325b5409579de04165ee))
* **logging:** 🐛 improved server redirection logging ([1755d14](https://github.com/caunt/Void/commit/1755d14605e8f3260a2288ec309087850be57faa))
* **logging:** 🐛 include direction in error logs ([c083ed7](https://github.com/caunt/Void/commit/c083ed71a46232e40878e82b08db05056927bad7))
* **logging:** 🐛 streamline logging configuration ([8ac0937](https://github.com/caunt/Void/commit/8ac0937af00f68e170297c9dc1e467fa30baf9c2))
* **logging:** 🔧 add default case for packet logging ([33d8679](https://github.com/caunt/Void/commit/33d8679e64bf8195794b0fe248a875ff1e4f3c94))
* **logging:** 🔧 add trace for link stopping reason ([150d48f](https://github.com/caunt/Void/commit/150d48f722d39743b413976d06b96df467204a16))
* **logging:** 🔧 enhance log message with direction ([b7f6c4d](https://github.com/caunt/Void/commit/b7f6c4d166b06c51cc447715d35ae2bc1cf67c94))
* **logging:** 🔧 override microsoft logging level ([aae26de](https://github.com/caunt/Void/commit/aae26de6dfab8fb5a2dcee59d5eb5dc80712cd61))
* **logging:** 🔧 standardize logging across services ([3268348](https://github.com/caunt/Void/commit/326834867deecf99764ecea8eb7ab0752659a5e3))
* **logging:** 🔧 update plugin name in log message ([5a3efd4](https://github.com/caunt/Void/commit/5a3efd44c2f8e36c304fa5e01f6b91841077348b))
* **minecraft:** 🐛 correct typo in CommandDispatcher variable name ([5bc40d9](https://github.com/caunt/Void/commit/5bc40d9830fbe09d84cb03d8197e80cf3ac9f40c))
* **mods-support:** ✨ prepare for forge support plugin ([50460de](https://github.com/caunt/Void/commit/50460de853ba0b87d36e0e5f3f9e88ee1845da0b))
* **network:** 🐛 handle only valid sides & improve logging ([7145709](https://github.com/caunt/Void/commit/71457090367ede1b71836af1e68312b1b39f14f5))
* **network:** 🐛 specified exact operations to register on phase changes ([ae0bf7e](https://github.com/caunt/Void/commit/ae0bf7e266b678452ebc4df6160f692c4d25c73c))
* **network:** 🐛 suppress stream closed exception for kick api ([8d58334](https://github.com/caunt/Void/commit/8d58334583597b2dfd4e44aae207a5513e224564))
* **network:** 🔥 removed origin from channel APIs ([55104dd](https://github.com/caunt/Void/commit/55104ddd829a891e3f51b3ea869ce82bade2ff8b))
* **network:** 🔧 split plugin packet id registries by operation type ([a79b79e](https://github.com/caunt/Void/commit/a79b79e6e0689f32eb0c5915411c762109b05c75))
* **packets:** ✨ allow to set direction manually to register packets ([f140cfa](https://github.com/caunt/Void/commit/f140cfab007609dd9d626f8d823ab9434f7c2a7d))
* **platform:** 🐛 add null-safety checks for option aliases ([be31e65](https://github.com/caunt/Void/commit/be31e65a146c1594dbe413208583d58cfca08ada))
* **platform:** 🐛 parse interface option from string ([c7ebec4](https://github.com/caunt/Void/commit/c7ebec44338bc440b43af374f2e652d8762157e0))
* **platform:** 🐛 prevent collection modification during enumeration ([40a9900](https://github.com/caunt/Void/commit/40a9900cc3757e306e95460e1d50f7f6cd4b692c))
* **platform:** 📝 clarify file watcher comment ([b1973cb](https://github.com/caunt/Void/commit/b1973cba5e2805ebe64f886539ddcf42f0af099d))
* **platform:** 🔒 use AsyncLock for thread-safe option discovery ([ba6d86d](https://github.com/caunt/Void/commit/ba6d86ddc99629343f081cd6d1492c71ed30398f))
* **platform:** 🧵 isolate log and console writers per instance ([6a942ec](https://github.com/caunt/Void/commit/6a942ece1b3bfe21376319827b00e93377f9fca2))
* **player-extensions:** 🐛 allow serverbound packets to be registered without a link ([b5c8f11](https://github.com/caunt/Void/commit/b5c8f11a250a325eb37ef7b8422872e874146685))
* **player:** ✨ return early after direction detection ([77bca1e](https://github.com/caunt/Void/commit/77bca1ebf9bae89e1c9458925f07cf4183513881))
* **player:** 🐛 prevent unwanted player reconnection ([5cc3260](https://github.com/caunt/Void/commit/5cc3260e39c35e82725fc07aa5fb454cb463e981))
* **plugins:** 🐛 handle file URLs as local plugins ([3f3f290](https://github.com/caunt/Void/commit/3f3f290495f425e3289308d87c8f16be3e081340))
* **plugins:** 🐛 handle invalid plugin urls and paths ([2f32b45](https://github.com/caunt/Void/commit/2f32b45b3561899e0c7f90cf2d360597f1eb8ddc))
* **plugins:** 📝 correct NuGet resolver comment ([e94921c](https://github.com/caunt/Void/commit/e94921ce81a5971ee61bb5f17109b633cc924398))
* **plugins:** 🧹 ensure aggressive GC for unload ([32ccc4a](https://github.com/caunt/Void/commit/32ccc4a724a13f06a7a7617a09b298894a810393))
* **promptreader:** 🐛 remove redundant buffer update ([87cb233](https://github.com/caunt/Void/commit/87cb233c21182be34975b22e313878acd1afc90a))
* **properties:** 🐛 improve WorkingDirectory assignment ([da47429](https://github.com/caunt/Void/commit/da47429de52bd532b4d599ecde86a54c50e6ce58))
* **proxy:** ✨ clear only affected packet registries on phase changes ([6e5c57d](https://github.com/caunt/Void/commit/6e5c57d8124998f31fbbe51e774721b36d4b4f52))
* **registries:** 🐛 update player phase at last post order ([b42e61b](https://github.com/caunt/Void/commit/b42e61b75c8bf99b8ade671c330d954667aef762))
* **registry-service:** 🐛 clear registries early on phase change ([36e1814](https://github.com/caunt/Void/commit/36e18146b8b3095f3a0d4fea21a8e5ec87a1bf0d))
* **registry:** 🐛 clear registries only by current protocol support plugin ([8467dd5](https://github.com/caunt/Void/commit/8467dd56ea79c42aa8d5d162ba611ccff447f056))
* **registry:** 🐛 prevent enumeration of wrong registries for possible plugin packet codecs ([cf5163b](https://github.com/caunt/Void/commit/cf5163bcfb78b64fc91c50a0e83e12c9f8bfcdd8))
* **registry:** 🧹 clear less registries on phase changes ([2328c02](https://github.com/caunt/Void/commit/2328c02edc1a55b07d31ee3c9ef2ad815bf21446))
* **resolver:** 🐛 improved assembly matching and logging ([67aa38a](https://github.com/caunt/Void/commit/67aa38a305740333723bcb78aad6170a997d5263))
* **run-options:** ✨ add missing separator to working directory path ([6a03b69](https://github.com/caunt/Void/commit/6a03b6922cb9797aa2a0599c9b4958930addf314))
* **servers:** ✨ log registered servers on startup ([77b7b83](https://github.com/caunt/Void/commit/77b7b839ba9ff7dd6e9f65aea95052c11cfc5108))
* **tests:** ♻️ inherit environment variables in spawned processes ([4424c1b](https://github.com/caunt/Void/commit/4424c1b704e02ddbf3fa430ad5d657b8e101a20a))
* **tests:** 🐛 access build property in builds array ([4fb8738](https://github.com/caunt/Void/commit/4fb87384151c28e661b881050018cfd6a6969ed7))
* **tests:** 🐛 add contact URL to HttpClient User-Agent for PaperMC API ([9910a51](https://github.com/caunt/Void/commit/9910a51240db7f5dc4d01f6f308355ff1d19efea))
* **tests:** 🐛 add fallback for edge case with all pre-release versions ([24f6cc7](https://github.com/caunt/Void/commit/24f6cc7f56dc605f3b0be0db6fd043c49762fa77))
* **tests:** 🐛 add missing /builds to PaperMC API endpoint ([0945fc4](https://github.com/caunt/Void/commit/0945fc4a6fd60af9553f3f221f75da87bc572927))
* **tests:** 🐛 correct LINQ lambda expression for suffix filtering ([6b2540a](https://github.com/caunt/Void/commit/6b2540ae6ce1ffeaee8d20364a982e53b4747bb2))
* **tests:** 🐛 filter out pre-release versions and use correct API endpoint ([5b39211](https://github.com/caunt/Void/commit/5b392115d430db34d5ddbc033fef763be48981f9))
* **tests:** 🐛 update PaperMC API endpoint to include /versions path ([0c96ffa](https://github.com/caunt/Void/commit/0c96ffada627cfb29980c8cceb864429cb0f1335))
* **trace:** ✨ enhance event handling order ([e414cc5](https://github.com/caunt/Void/commit/e414cc563e9c6ec70d45b7f5b9e807234e635259))
* **transformations:** ✨ register transformations after respawn packet ([a60752b](https://github.com/caunt/Void/commit/a60752b070fba136bb85e583faba569fbaf981a7))
* **velocity:** ✨ fixed packet registration ([ee92bc7](https://github.com/caunt/Void/commit/ee92bc7864ff96191b3416446a9bd620ade63749))


### Performance Improvements

* **buffers:** ⚡ accept ReadOnlySpan&lt;char&gt; in WriteString ([1463242](https://github.com/caunt/Void/commit/1463242bca8a930aa5786adcf5b6c7809ee001a0))
* **commands:** ⚡️ avoid lower-case allocation in bool suggestions ([7e13124](https://github.com/caunt/Void/commit/7e131244d09fbed907e90ddc026b8f3c42ab1832))
* **commands:** ⚡ avoid lowercase allocation ([9be6bb2](https://github.com/caunt/Void/commit/9be6bb2311c48d6269a46a3fffb7d84d36eef33a))
* **common:** ♻️ use span-based stream write ([c0de8b5](https://github.com/caunt/Void/commit/c0de8b5543563de02480707ebe0819d1644d321d))
* **common:** ⚡ replace BitConverter with BinaryPrimitives ([6cd134d](https://github.com/caunt/Void/commit/6cd134dafd2ad9403912ef9338273cefb184ff34))
* **components:** ⚡ avoid params allocation in legacy serializer ([84965c8](https://github.com/caunt/Void/commit/84965c86d781ffaa474af4bc157cd2b3e74cdf02))
* **components:** ⚡ use span for hex color check ([e871e0e](https://github.com/caunt/Void/commit/e871e0ee43568568284c22429a2257d355f580d4))
* **forwarding-support:** ♻️ avoid split allocation ([326711c](https://github.com/caunt/Void/commit/326711c109eb8269676574dabb2aad7ca39365b2))
* **forwarding-support:** ⚡ eliminate string allocation ([a8fafe9](https://github.com/caunt/Void/commit/a8fafe95239324eeadbe4c41545f7d887f4b05a7))
* **forwarding:** ⚡️ cache maximum forwarding version ([904894c](https://github.com/caunt/Void/commit/904894c65b94bcb1b54d6a0b8f42c841bbd41e28))
* **minecraft:** ♻️ avoid StartsWith for color parsing ([4d4fae8](https://github.com/caunt/Void/commit/4d4fae8f8d94747394b94c2dc42e7da730af9076))
* **minecraft:** ♻️ avoid substring allocation in literal command node ([1d79daa](https://github.com/caunt/Void/commit/1d79daa78af176185c55345cc29278dfd7d2fbbc))
* **minecraft:** ♻️ use stackalloc for VarLongProperty ([50f1919](https://github.com/caunt/Void/commit/50f1919d74ebef912b3921abdefc27af9aa85c78))
* **minecraft:** ⚡ avoid array copy when creating BinaryProperty from stream ([f59fd0d](https://github.com/caunt/Void/commit/f59fd0d33362f10e42103e64fb59a948fe8e442f))
* **minecraft:** ⚡ avoid array copy when serializing NBT property ([cbf8b4b](https://github.com/caunt/Void/commit/cbf8b4beb2be51174a7745e553769fa464cd1108))
* **minecraft:** ⚡ avoid substring allocation in long parser ([53f1c73](https://github.com/caunt/Void/commit/53f1c733fa58e71c533d1218abdf6c37320d0758))
* **network:** ♻️ avoid extra allocation in StringProperty ([f5b6db2](https://github.com/caunt/Void/commit/f5b6db2458b1b7b1fe6bf03c22a916bd2a0ab0a6))
* **plugins:** ⚡ use span copy for IV rotation ([8535957](https://github.com/caunt/Void/commit/8535957431470ab4580b488226ef56a544a2e7a1))
* **tests:** ⚡ avoid params allocation in CollectingTextWriter ([0f304c1](https://github.com/caunt/Void/commit/0f304c1e90ab04698823c2fd1e46dc87bde608f2))

## [0.5.4](https://github.com/caunt/Void/compare/v0.5.3...v0.5.4) (2025-08-01)


### Features

* add cancellation support to entry point ([e68e37b](https://github.com/caunt/Void/commit/e68e37b350be559ba17b76b7de5e790fc7adecb8))
* add test project reference to solution ([9774e0b](https://github.com/caunt/Void/commit/9774e0bbc5a25ba60def3aca3d5ad63f6fa08397))
* add TryParse for Uuid ([caf19c6](https://github.com/caunt/Void/commit/caf19c6310f8510c72c51323cd9dd94c737c6d9c))
* allow specifying custom log writer ([7a77295](https://github.com/caunt/Void/commit/7a77295ce73f4a64a5bc130533db0e31554ac2fa))
* **auth:** ✨ add offline mode ([6c856fc](https://github.com/caunt/Void/commit/6c856fce5dec082c8360055d4112fd2b9de086a4))
* **docs:** expand SEO keywords ([8c07367](https://github.com/caunt/Void/commit/8c073672804d436133cbf1161e2f2d08354f189b))
* **docs:** improve SEO metadata ([3a4684f](https://github.com/caunt/Void/commit/3a4684f89ad753488e651ad69008c3545fdd9809))
* **encryption:** ✨ add support for offline mode ([c1afcd6](https://github.com/caunt/Void/commit/c1afcd68356d31fc856fe675bbdbfa5d7fada0ca))
* **platform:** ✨ add interface CLI option ([10746b5](https://github.com/caunt/Void/commit/10746b5711bfc3c850499ad13c0e2ea72f593661))
* **platform:** ✨ add logging level option ([032b3f8](https://github.com/caunt/Void/commit/032b3f8a6883e5be85b166996cc0fe491b8209a6))
* **platform:** ✨ add port CLI option ([2ff4089](https://github.com/caunt/Void/commit/2ff40896f93326d7a08fa0e90565d7f9e05408ce))
* **platform:** ✨ add server CLI option ([1901aff](https://github.com/caunt/Void/commit/1901aff9176ae0ab37f18995548d6cc01372d6f3))
* **platform:** ✨ name CLI servers sequentially ([b852e5e](https://github.com/caunt/Void/commit/b852e5ed6ed7869ea2a10daad963179545052868))
* **platform:** add ignore-file-servers option ([3aa41c2](https://github.com/caunt/Void/commit/3aa41c24ef91c42ae72ba48ae5c61ca8a1048dd7))
* **protocol:** ✨ add new protocol versions ([e1a76ec](https://github.com/caunt/Void/commit/e1a76ec9f22bbb296847eb25284a60eee4282e8a))
* **protocol:** ✨ improve string representation of names ([3bedd1a](https://github.com/caunt/Void/commit/3bedd1ab70eb432d50fbe36c5275583789c47e7c))
* **registry:** ✨ update mappings for new protocol versions ([9776ccb](https://github.com/caunt/Void/commit/9776ccb2a44e3bf22e395fc7048b079be310ae1b))
* **servers:** ✨ validate server arguments ([2690724](https://github.com/caunt/Void/commit/269072471796309eaebc11f3c0e17ae425a8b1d8))
* **tests:** ♻️ reset integration logs before each run ([563a2da](https://github.com/caunt/Void/commit/563a2da7c99bbd51b769cd6cb6904cdd78805f25))
* **tests:** use JRE 21 for Minecraft integration ([e165115](https://github.com/caunt/Void/commit/e1651159dc57d383999e32f930d70aefc02b280a))


### Bug Fixes

* add to RunAsync cancellation support ([db1fc77](https://github.com/caunt/Void/commit/db1fc7724be7e57b6a684762f5b8a8071f8457ac))
* **benchmarks:** 🐛 remove null-forgiving operator ([8dd5cb7](https://github.com/caunt/Void/commit/8dd5cb790efca1d00cf80d9c279adfdef8a8bf3c))
* **buffers:** 🐛 correct memory stream length comparison ([4a631b1](https://github.com/caunt/Void/commit/4a631b1d982581438dbafa07bc5a0341a2d838dd))
* **ci:** set GitHub token for test steps ([5d62062](https://github.com/caunt/Void/commit/5d620622239e9d037a383d51b1a0e5342c8f112a))
* **common-network:** 🐛 handle disposed stream ([4503d70](https://github.com/caunt/Void/commit/4503d70745c817e889bffb14522dfc999d719fd5))
* **common:** 🐛 correct invalid cast message ([bbe7748](https://github.com/caunt/Void/commit/bbe7748c90b885dc13ca2061254ddd1bc4fc1852))
* **common:** 🛠️ remove null-forgiving operator ([2ca845e](https://github.com/caunt/Void/commit/2ca845e67a45ae78f74af4ac53bcf9dc38b8bd02))
* **components:** ♻️ write nbt as unnamed after 1.21.5 ([aead5bf](https://github.com/caunt/Void/commit/aead5bf3d3784193e48c7203b9463e58578ef63a))
* correct error message formatting ([0d4a8eb](https://github.com/caunt/Void/commit/0d4a8eb0650e4681abdcd55f2b99501d6635dade))
* correct java binaries variable name ([ba487cd](https://github.com/caunt/Void/commit/ba487cd68ff5c0ebc33e8969c47a8cc4a3b00d98))
* correct typos in code ([1559b90](https://github.com/caunt/Void/commit/1559b90919abab1d6f9ab63be20602e54dd34de3))
* correct VarLong and UUID int array handling ([c4d6e83](https://github.com/caunt/Void/commit/c4d6e833587b48cca71b06cf18205f017779540f))
* **dependencies:** 🔧 update dependencies ([470135e](https://github.com/caunt/Void/commit/470135eb5c0f9feae9a3d860a83fb38fae4618e4))
* **docs:** 📝 separate distribution header ([06a1ff0](https://github.com/caunt/Void/commit/06a1ff02f3bf9789b17eded54b4c0e7687e7efe1))
* **docs:** 🔒 authenticate release fetch ([1fe57ab](https://github.com/caunt/Void/commit/1fe57ab1e48802c44131a5e8532cf107930d8748))
* **docs:** correct grammar in features guide ([c2253de](https://github.com/caunt/Void/commit/c2253de3b39b5f5a676438c071898e0664048774))
* encapsulate entry point logic in EntryPoint class ([850971c](https://github.com/caunt/Void/commit/850971c80c5ac25e61045bafefdcf978a6db5864))
* **errors:** 🐛 clarify packet registration error message ([0425a60](https://github.com/caunt/Void/commit/0425a60d46562bd9ff0814fb41d6206f370d50fc))
* **events:** 🐛 remove null forgiving operator ([9761b49](https://github.com/caunt/Void/commit/9761b49bdd91469b0fa97af8c43d59eb9c0ad2d4))
* improve console input/output handling ([61d2219](https://github.com/caunt/Void/commit/61d22190b821192a9f183f338517bef5e1162e58))
* **links:** 🐛 correct comment typo ([9e8e34f](https://github.com/caunt/Void/commit/9e8e34fcc66633d4f4cf435564c4dcc54452f94a))
* **logging:** 🔧 enhance connection log details ([661c9bf](https://github.com/caunt/Void/commit/661c9bfff5e81e002b0912d804f95f650b514ac8))
* **logging:** 🔧 improve logging level assignment ([2ad7cfb](https://github.com/caunt/Void/commit/2ad7cfb922b33331bab8db30e593a60ba4e94289))
* **minecraft:** 🐛 correct float array color precision ([4ba0d4a](https://github.com/caunt/Void/commit/4ba0d4a59fc14260523067811c25ef0491cf2c3c))
* **minecraft:** 🐛 correct grammar in slice exception message ([8b1f1d0](https://github.com/caunt/Void/commit/8b1f1d0dd7986dc30285aae9fb2cbc50bad230bb))
* **mojang:** 🔧 update offline status handling ([825ebca](https://github.com/caunt/Void/commit/825ebca25c1ebf878072acbe21bbfdafe8d00f77))
* **nuget:** 🐛 select newer versions correctly ([e7fc187](https://github.com/caunt/Void/commit/e7fc187a1c046211acfbbe46bd8041f9377f8292))
* **packets:** 📝 temporary disable SystemChatMessagePacket transformations ([e86db88](https://github.com/caunt/Void/commit/e86db88499afdd6f52e2a8ff09eb70a89faa4db7))
* **platform:** ♻️ reinitialize logger for each run ([47e2a0b](https://github.com/caunt/Void/commit/47e2a0bd1950dde5a92c8489f9b374928f587215))
* **platform:** ♻️ update offline mode configuration ([3acbd40](https://github.com/caunt/Void/commit/3acbd40885d723a8cc4b5bd5634e8a56a2e2d3bc))
* **platform:** 🔧 use offline mode CLI option value ([0fb47cf](https://github.com/caunt/Void/commit/0fb47cfde640cb2483a132aa6bb4abc0c03a783d))
* **platform:** 🔨 avoid null-forgiving operator ([8b1ba03](https://github.com/caunt/Void/commit/8b1ba033dd520aad79781d2f46c3352e64222217))
* **plugin:** 🐛 make linq callback async ([1bbbdfc](https://github.com/caunt/Void/commit/1bbbdfc88f79a2970ada027af3d5dbaa497012e4))
* **plugins:** 🐛 remove braces for single-line lock ([8eec7ef](https://github.com/caunt/Void/commit/8eec7efc0a011a13333c31eb7e34a44cba91878d))
* **plugins:** 🩹 remove null-forgiving operator usage ([fff0888](https://github.com/caunt/Void/commit/fff0888db4e2a0673dde284f3239911c4c7fa369))
* **plugins:** use injected HttpClient directly ([841d677](https://github.com/caunt/Void/commit/841d67789cd9a6569750381d776ae2cc4b40fb61))
* prioritize env var setting over in-file configuration ([15fa575](https://github.com/caunt/Void/commit/15fa5751154a287da2a0cb1a0448b6c2b0c99796))
* **protocol:** ✨ update string representation of versions ([41a4d71](https://github.com/caunt/Void/commit/41a4d71ca602017f73dde1007068e84916407591))
* **protocol:** 🔧 update signature data handling ([ac953bc](https://github.com/caunt/Void/commit/ac953bcca23b0c3ca51ba2b4365f4bfda75484a9))
* **protocolsupport:** remove null forgiving operator from login packet \U0001F4DD ([cc7135c](https://github.com/caunt/Void/commit/cc7135cf6ea91d96ebdee33a1ab018c037056c88))
* **registry:** 🐛 correct varlong property encoding ([111f1bf](https://github.com/caunt/Void/commit/111f1bf1636185994a70b309440ef1c05baa344c))
* **servers:** 🔧 fix --ignore-file-servers option usage ([9b5d814](https://github.com/caunt/Void/commit/9b5d81419eb4c95ea20f546b6067b20c02bdf3ea))
* **settings:** 🔧 update Offline property for runtime use ([07be838](https://github.com/caunt/Void/commit/07be838181a3abc3fbb47f3e3db4fc5a6d7c2c6b))
* simplify hash computation in GuidHelper ([1ea7f98](https://github.com/caunt/Void/commit/1ea7f98d591466f65e0597d005cf31a204c4f6e2))
* **terminal:** 🩹 remove unsupported constructor ([c81855c](https://github.com/caunt/Void/commit/c81855cb6d39359760b401e750f5c184b7355f4b))
* **tests:** ♻️ use interactive HeadlessMC ([4e446b1](https://github.com/caunt/Void/commit/4e446b18b5c371395941dea5dc5328c47db19099))
* **tests:** cancel process reads in PaperMC test ([1621686](https://github.com/caunt/Void/commit/16216865346e47b28a2bb0f09af30321fc69c508))
* **tests:** correct paper working directory variable ([3311808](https://github.com/caunt/Void/commit/3311808f97c562db0c4af95b4cfcd4e416ae8ca1))
* **tests:** import MITM certificate for portable JRE ([5790d36](https://github.com/caunt/Void/commit/5790d36bc78087b408442aa7c0b56fc87a501718))
* **transformers:** ✨ update hoverEvent processing to support Minecraft Console Client behavior ([304ce72](https://github.com/caunt/Void/commit/304ce72ce320b5b0fdb1c039328a21296b5d1b9f))
* wait readiness first ([9c6e52c](https://github.com/caunt/Void/commit/9c6e52cb753d0d1f746db83616e60be6d03fee31))


### Performance Improvements

* **buffers:** ♻️ use stackalloc for primitive writes ([b313cd3](https://github.com/caunt/Void/commit/b313cd37adadd61125abb875a58f398a9488ee40))

## [0.5.3](https://github.com/caunt/Void/compare/v0.5.2...v0.5.3) (2025-05-25)


### Features

* added manually requested stop reason to link events ([1fdbb07](https://github.com/caunt/Void/commit/1fdbb072efefb6c743c8e6c05c8d0cbf5775bc2f))
* added reason to link stop events ([04a5c64](https://github.com/caunt/Void/commit/04a5c646384768a87c1d6d00ebbd99099fd5b7f0))


### Bug Fixes

* proxy awaits for bind address to release ([7d7e54c](https://github.com/caunt/Void/commit/7d7e54cf649d2671b3ebc5c67902d02aadce0950))
* read & write text components nbt as unnamed after minecraft 1.20 ([c8c2495](https://github.com/caunt/Void/commit/c8c24954cc4a4b75479adab2271cf1b9bc1615af))

## [0.5.2](https://github.com/caunt/Void/compare/v0.5.1...v0.5.2) (2025-05-24)


### Bug Fixes

* changed player disconnection path ([1c901e9](https://github.com/caunt/Void/commit/1c901e97f02bb7a98167d68209421563a0c5ebd6))
* handle online-mode servers with authentication side set to proxy ([3b38c96](https://github.com/caunt/Void/commit/3b38c96765f18d9d6e5405f3c42b195616af2247))
* nbt compound and list parsing are now in one method ([f9d524b](https://github.com/caunt/Void/commit/f9d524b7abc0e3738738843aa749594d0858d9d2))

## [0.5.1](https://github.com/caunt/Void/compare/v0.5.0...v0.5.1) (2025-05-16)


### Bug Fixes

* bump compiled release version ([493b174](https://github.com/caunt/Void/commit/493b174e2d991e554caacfc3af19385aae3ae4dd))

## [0.5.0](https://github.com/caunt/Void/compare/v0.4.0...v0.5.0) (2025-05-15)


### ⚠ BREAKING CHANGES

* update environment variables names

### Features

* added many watchdog api methods ([9670d18](https://github.com/caunt/Void/commit/9670d1821f0481205f056eb645b6eea236c32837))
* added VOID_WATCHDOG_ENABLE environment variable ([5bddb90](https://github.com/caunt/Void/commit/5bddb9018a0a7459c4ea5fbff85bea5ee37aa7f1))
* added watchdog simple web application ([b82da8a](https://github.com/caunt/Void/commit/b82da8ad5fb09e918ccf0a568284ad600950f77c))
* allow pausing accepting connections ([dc772e8](https://github.com/caunt/Void/commit/dc772e861e4057d39eabbf225249eb277f942eba))
* allow specifying address to bind watchdog ([2790e7a](https://github.com/caunt/Void/commit/2790e7af73708f8065e118187b088f91a9ef3095))
* allow specifying repositories with VOID_NUGET_REPOSITORIES environment variable ([627e243](https://github.com/caunt/Void/commit/627e2435d546d72d163e607879ec5f6f2315666d))
* allow specifying whether to kick players on proxy shutdown ([704e89b](https://github.com/caunt/Void/commit/704e89b7d69a522a704589a95e4beae1b3d358c4))
* discover servers automatically ([d700d6f](https://github.com/caunt/Void/commit/d700d6f8beae7287d6640f0b420219324e5bba01))
* include asp.net runtime assemblies into a build ([860ddc3](https://github.com/caunt/Void/commit/860ddc347c5205b9cc7d5102f42086ef1101a5fb))
* introduce watchdog plugin ([0fb4554](https://github.com/caunt/Void/commit/0fb4554db98de860500737a17f923168b09d7f4b))


### Bug Fixes

* added proxy statuses ([54a45ab](https://github.com/caunt/Void/commit/54a45abc3a820c9ed83e6fae3e182efc1a74d92d))
* added version property to all builds ([e690e1e](https://github.com/caunt/Void/commit/e690e1eac5c051b154972490869a4b0dcc6f932b))
* allow semicolon escaping ([fe2e1d4](https://github.com/caunt/Void/commit/fe2e1d421e41ccbe7b0d684390d7a7fee7f02250))
* allow terminals without tty ([7be8071](https://github.com/caunt/Void/commit/7be8071555474c0243e12b2fe1cf4fd9a442bc1e))
* cache toml mapped assembly types ([9b6c529](https://github.com/caunt/Void/commit/9b6c52974439cf7af5b2b1382e374cf6c108e1ee))
* conditional dotnet sdk ([70444ed](https://github.com/caunt/Void/commit/70444edcdf5fd4bf3a40cbfae5a3eb3c5b476b4b))
* conditional remove aspnet sdk ([c5a922a](https://github.com/caunt/Void/commit/c5a922ad93b901fa28a61fff41332024a216858e))
* configure hosting shutdown timeout to infinite ([b35ada2](https://github.com/caunt/Void/commit/b35ada2d95bb62e5f9dfad3e85727e4b14e2459c))
* custom conditional variable for bionic sdk switch ([7181430](https://github.com/caunt/Void/commit/71814300084261910e4802a2b288f7f3e5e0ff6d))
* dispose cancellation token source after event is handled ([d815c5d](https://github.com/caunt/Void/commit/d815c5d93772380e04131e8235e272360dddf4c6))
* do not include runtime identifier in solution build ([66255ad](https://github.com/caunt/Void/commit/66255add92df661f63e210b8f0f4d8958fac9ce1))
* do not read console when input is redirected ([3fd46b2](https://github.com/caunt/Void/commit/3fd46b27a4ca12f00fb9590116f1b21794fc0121))
* do not specify list of rids ([1af547c](https://github.com/caunt/Void/commit/1af547c62a4480ad469c77703a2e098246f9a3af))
* exclude watchdog plugin from bionic builds ([64cd99b](https://github.com/caunt/Void/commit/64cd99bc849fde7261d2705a0d1c5c362f37944f))
* include aspnet framework when not bionic ([75238e6](https://github.com/caunt/Void/commit/75238e62b3e869b95013a797464ac1c003d32b71))
* include rids via msbuild property argument ([0f7e6d6](https://github.com/caunt/Void/commit/0f7e6d6a0fcc2a15ed3777d97018721f0107d0c0))
* include sdk parameter in build ([90ed3bb](https://github.com/caunt/Void/commit/90ed3bb691a89a707ca0b4c01dffe87bcb0eca89))
* loading shared assemblies ([ab36bb0](https://github.com/caunt/Void/commit/ab36bb0679a9ea71c02f32f7672af722357e0653))
* print proxy version at start ([e82513b](https://github.com/caunt/Void/commit/e82513b6722f644725baabf0f016546ad81f26eb))
* pull assembly version from attribute ([4cd32ff](https://github.com/caunt/Void/commit/4cd32ff2460026e761aa0dec35bde23cf8be62c5))
* register proxy as hosted service ([89541b8](https://github.com/caunt/Void/commit/89541b80f175c23b775adf2025084682e96b29a7))
* rename manifest_version to fallback_version ([5a31032](https://github.com/caunt/Void/commit/5a31032c294b3b74c029ca6a392370f743ce1e06))
* update proxy interface to support manual stopping ([1ba2401](https://github.com/caunt/Void/commit/1ba2401ea8d1d526c45e7f031890a7ed6a904582))
* use latest release v tag in unstable builds ([2b96f58](https://github.com/caunt/Void/commit/2b96f585a4849e2a36d003ed4f0a297f3d4d9b1a))
* waiting for players to disconnect before emitting application lifetime stop ([b08b0ce](https://github.com/caunt/Void/commit/b08b0cefa1fdc892b8f54a88e15dfb2ae3055cbc))
* watchdog is not started anymore if `Enabled` settings is set to false ([5bddb90](https://github.com/caunt/Void/commit/5bddb9018a0a7459c4ea5fbff85bea5ee37aa7f1))


### Code Refactoring

* update environment variables names ([5bddb90](https://github.com/caunt/Void/commit/5bddb9018a0a7459c4ea5fbff85bea5ee37aa7f1))

## [0.4.0](https://github.com/caunt/Void/compare/v0.3.4...v0.4.0) (2025-05-08)


### ⚠ BREAKING CHANGES

* dependency resolvers interface

### Features

* include nuget repositories from command line options ([a8421bc](https://github.com/caunt/Void/commit/a8421bc9de8cedec693f70845f86d85831e036a6))
* parameters injection in service instantiation ([a8f777a](https://github.com/caunt/Void/commit/a8f777a469abbddaf5230a3400233757cb362bb3))
* register custom nuget repositories option ([e3ba723](https://github.com/caunt/Void/commit/e3ba723417a3cb6c0faf135fff2c9c731a4f3127))


### Bug Fixes

* allow adding repositories from plugins ([75a2159](https://github.com/caunt/Void/commit/75a215985e9fd55d29832140a561af3e3da66a0d))
* allow loading directories with plugins ([b81e0ab](https://github.com/caunt/Void/commit/b81e0ab7c23fb21de0e2ec873ee4e90a13600640))
* collection modified ([4d55cae](https://github.com/caunt/Void/commit/4d55caec72be9813254cbdc1ea401626cbf627da))
* conditional else ([165f88e](https://github.com/caunt/Void/commit/165f88e432d0aa1301c1476003c6d79f478e88ba))
* configure commandline with default options ([763274d](https://github.com/caunt/Void/commit/763274d8b81171c55238be5e889149b2c2afe7a3))
* custom repositories nuget authentication ([1f36778](https://github.com/caunt/Void/commit/1f36778268a78587751fe5c11c7157a5f55ddfb9))
* do not create services instances manually ([f2234b1](https://github.com/caunt/Void/commit/f2234b183222d83a8693a6c613079d62c7e1e4ee))
* enable more default commandline options ([71e44f5](https://github.com/caunt/Void/commit/71e44f5b93fa920c9a812047308bf82381aa0c60))
* register options via commandline ([94b425e](https://github.com/caunt/Void/commit/94b425e9038d745efc85438751b191e0c2636be5))
* update plugins option name ([7673b4e](https://github.com/caunt/Void/commit/7673b4ec6aeea5764363364ef3d3804d5758ffe0))


### Code Refactoring

* dependency resolvers interface ([1eec2b9](https://github.com/caunt/Void/commit/1eec2b9d0300cfaf2e8bfdc2e97676e51c4571d2))

## [0.3.4](https://github.com/caunt/Void/compare/v0.3.3...v0.3.4) (2025-05-06)


### Bug Fixes

* add plugins layer to player context composite ([5600129](https://github.com/caunt/Void/commit/560012989dee5ee7f24150945af798f53c003b65))
* allow empty components deserializing ([0d0b255](https://github.com/caunt/Void/commit/0d0b2556a1f9ec08cb0079e878d1907856bde7bc))
* apply convert attribute on uuid ([dac32a2](https://github.com/caunt/Void/commit/dac32a2fb0cfce027a4d6fa13d54bcfd94399940))
* do not update configuration files from disk if they were just saved ([54e01c5](https://github.com/caunt/Void/commit/54e01c53b72b9e44d35b68c24986e44282618639))
* mojang service authentication ([236a403](https://github.com/caunt/Void/commit/236a40350118e5bc7c9fa8b2262581005a6c90a1))
* read and write as property names for uuid converter ([74b5854](https://github.com/caunt/Void/commit/74b585452b8ad36a98b151ede7ede07cceb6385c))
* replace updates skips with half second cooldown ([be9a358](https://github.com/caunt/Void/commit/be9a3580114e235a245de97799e6a773ae8de949))
* synchronize configuration loading ([b12b128](https://github.com/caunt/Void/commit/b12b128a27e90f00f9a1cf5a57c66efcd96d8da8))
* synchronize configuration service even more ([a131819](https://github.com/caunt/Void/commit/a1318195bee43095dd9f899c900710d1a518c2cb))

## [0.3.3](https://github.com/caunt/Void/compare/v0.3.2...v0.3.3) (2025-05-04)


### Bug Fixes

* docker image tags single quotes ([e922c85](https://github.com/caunt/Void/commit/e922c85eb4f8dccd27fcc1875eaefc1c947b2ad7))

## [0.3.2](https://github.com/caunt/Void/compare/v0.3.1...v0.3.2) (2025-05-04)


### Features

* configurable velocity forwarding ([1a64fb3](https://github.com/caunt/Void/commit/1a64fb381c487babf13e2734b7975635085a472c))


### Bug Fixes

* build configuration cache from existing files ([7dec9d7](https://github.com/caunt/Void/commit/7dec9d794ccf7ce30b96a99b2e99269f2c9a7226))
* ensure player is disposed after kick ([f70cbbc](https://github.com/caunt/Void/commit/f70cbbc72b37d7d504777f6e72ad88b834512170))
* make velocity forwarding packets be decoded on plugin side ([b741357](https://github.com/caunt/Void/commit/b74135739df8ea642f4df45850c885f15da6fa4c))
* register plugin container before loading event ([55473e3](https://github.com/caunt/Void/commit/55473e32c9ccfef1e8a944074bbd18f2ea8808d1))
* send feedback about unloaded container ([276aafc](https://github.com/caunt/Void/commit/276aafc7b2e7badd8ab53002a8e76c1475be731d))
* velocity forwarding 1.19.* versions ([2cb6a9c](https://github.com/caunt/Void/commit/2cb6a9c063e030ae0ee2e77bbd4c4d90442c25b4))
* velocity forwarding enabled check ([0330b99](https://github.com/caunt/Void/commit/0330b99721ad81c5435b7060fbb9e0718d506b89))
* write string length in span buffers ([90be225](https://github.com/caunt/Void/commit/90be225da7b3c26278743ed8f123d6c3c80d725d))

## [0.3.1](https://github.com/caunt/Void/compare/v0.3.0...v0.3.1) (2025-05-04)


### Bug Fixes

* allow passing cancellation token to event listeners ([c89dfc1](https://github.com/caunt/Void/commit/c89dfc1f44127cb9ab91c0ef68adae7fbffbc2d3))
* allow registration event listeners without cancellation token ([fe1a4ff](https://github.com/caunt/Void/commit/fe1a4ffef54a86cc357e240df29da66652845575))
* dispose minecraft player context ([9dd962c](https://github.com/caunt/Void/commit/9dd962c0aad78628798e5068de308aff2f359c81))
* do not kick player twice ([eb1dddc](https://github.com/caunt/Void/commit/eb1dddce34be240638670c349230d92cd64c9f6f))
* example plugin commands registration ([b26a5cd](https://github.com/caunt/Void/commit/b26a5cdc4834910f45c2b7703d404a3fc987c49b))
* isolate runtime assembly resolver ([4969583](https://github.com/caunt/Void/commit/49695830bda47f2bc0cead299809522460b136af))
* make dependencies cancellable ([883342f](https://github.com/caunt/Void/commit/883342f06fa776d289fd95313f21af6e71da3b4d))
* move brigadier extension types to same namespace ([b26a5cd](https://github.com/caunt/Void/commit/b26a5cdc4834910f45c2b7703d404a3fc987c49b))
* override console name ([4daa833](https://github.com/caunt/Void/commit/4daa833ef1b8023f9c29b624f41a3797b7b3ae0a))

## [0.3.0](https://github.com/caunt/Void/compare/v0.2.3...v0.3.0) (2025-05-03)


### ⚠ BREAKING CHANGES

* remove IMinecraftPlayer interface, move all minecraft extension methods to IPlayer directly

### Features

* gh pages documentation setup ([b6fb947](https://github.com/caunt/Void/commit/b6fb94753bdfe93956629c8d601e86ca9c179726))
* setup docs pages ([2aaded2](https://github.com/caunt/Void/commit/2aaded23555be7a68f1d121b514d5e306c015af4))


### Bug Fixes

* gh pages permissions ([5ab9d61](https://github.com/caunt/Void/commit/5ab9d61599df9002dca39a09603f06b6e6aee38a))
* gh pages upload path ([ad6d638](https://github.com/caunt/Void/commit/ad6d638c088e2c03b1119036dc6d04ede09df6a0))
* msdi documentation link ([1e1bf3b](https://github.com/caunt/Void/commit/1e1bf3b96b6be6786a15d5936c1efe11a7ef5157))
* temporary rollback C# 14 extension type ([680a580](https://github.com/caunt/Void/commit/680a580486d19b9548bc347f47d24ae3b6c266fa))


### Code Refactoring

* remove IMinecraftPlayer interface, move all minecraft extension methods to IPlayer directly ([a240a1d](https://github.com/caunt/Void/commit/a240a1d24757750e9b7af5750d0565dc5d69d6a6))

## [0.2.3](https://github.com/caunt/Void/compare/v0.2.2...v0.2.3) (2025-04-28)


### Bug Fixes

* cleanup plugin devkit ([9883c29](https://github.com/caunt/Void/commit/9883c292a4e3cd2b831707955667b88fd5fc776d))
* more crossplatform plugin devkit ([eec8107](https://github.com/caunt/Void/commit/eec81071ee006f1f36b1f0e7d331ed55385fdbb0))

## [0.2.2](https://github.com/caunt/Void/compare/v0.2.1...v0.2.2) (2025-04-28)


### Features

* include plugin development kit in release ([4ac5930](https://github.com/caunt/Void/commit/4ac5930791c0d1904267bf7a6399171b4d5af06a))


### Bug Fixes

* plugin devkit properties evaluation ([bd07714](https://github.com/caunt/Void/commit/bd077141fdbbc2d5aa9d3cedd88fb94c5de86349))
* remove launchSettings.json from plugin devkit ([aebe396](https://github.com/caunt/Void/commit/aebe396e112cc5ed6e3124601b4e1cc1f4d7c9e1))

## [0.2.1](https://github.com/caunt/Void/compare/v0.2.0...v0.2.1) (2025-04-27)


### Features

* loading environment plugins ([97e300a](https://github.com/caunt/Void/commit/97e300acb98587c60497987caaf81d3205b040c4))
* plugin devkit ([20382bf](https://github.com/caunt/Void/commit/20382bf8d24c33fe60bf01d7ee344ca96f23b132))
* switch example plugin dependencies to nuget source ([5eaa7dd](https://github.com/caunt/Void/commit/5eaa7dd0f4d8e080dfbd8b342c852d66e6475cc3))


### Bug Fixes

* concurrent configurations map ([e93e995](https://github.com/caunt/Void/commit/e93e9950fb7a3addc18f1e6097e6208914f98b49))
* print reflection type load exceptions ([a5a8624](https://github.com/caunt/Void/commit/a5a86245bd51a87d0393741994c8e9ccfc00b254))

## [0.2.0](https://github.com/caunt/Void/compare/v0.1.0...v0.2.0) (2025-04-26)


### Features

* add clear plugin methods to registries ([d641d10](https://github.com/caunt/Void/commit/d641d10a98b8c863434363a2926e9506122e2bce))
* add IsDisposed property to IPlayerContext ([2555af7](https://github.com/caunt/Void/commit/2555af72e8a1e6237a93a3d99d1660dcbcbbf5b5))
* add PAPER_CONFIG_REPO environment variable for configuration ([7734d2a](https://github.com/caunt/Void/commit/7734d2a73f19a3252957f691d5084745b7d28602))
* add runtime identifiers to all projects ([4a41bcc](https://github.com/caunt/Void/commit/4a41bcc9e2f4ee83ffb8f299e537c218105f556e))
* add Source property to ListeningServiceProvider ([eb27de7](https://github.com/caunt/Void/commit/eb27de708c16e799efe180d15765feb98621ff10))
* allow injecting non-generic ILogger ([9fc8fba](https://github.com/caunt/Void/commit/9fc8fbaf5e499196a004f2d54a3dcafc70ff33d9))
* allow injecting player context in scoped services ([e0a1fc9](https://github.com/caunt/Void/commit/e0a1fc9b551be0d1fe965290630226cba8b362e6))
* composite services for players ([c603085](https://github.com/caunt/Void/commit/c603085f4b0252cc09424109e5da25addccf24cd))
* create per-player scopes in dependency service ([3a5fb1e](https://github.com/caunt/Void/commit/3a5fb1efe5d2536d1a906651e754f9ce1f138a66))
* detection if player is a minecraft player, upgrading its implementation ([569c5ef](https://github.com/caunt/Void/commit/569c5ef710c83ebd812588587d7330f9e01444e7))
* dry ioc container tracking ([0d68799](https://github.com/caunt/Void/commit/0d68799fc69ad2650b61bbc96751d742c3306059))
* enable preview language features ([4ee7183](https://github.com/caunt/Void/commit/4ee7183e3d74c3962f7beb91cee1181dabc8fbd3))
* enhance DependencyService for better service management ([0e9a6b2](https://github.com/caunt/Void/commit/0e9a6b270acbc30e66aa50b9103aecda6ccb9770))
* enhance scoped service registration in DependencyService ([77ac2eb](https://github.com/caunt/Void/commit/77ac2ebdb664a622ee704a6e087d78d81fc5622e))
* enhance service registration in HostingExtensions ([cbac74a](https://github.com/caunt/Void/commit/cbac74a6beb310ddcf8c36f5b6546133d1b391ae))
* extension methods for synchronizing collections ([f2a1493](https://github.com/caunt/Void/commit/f2a14932752db501fb92fd1f2f44c7a9426d7cc5))
* filtering and bypassing the filter of scoped player events ([1a578e0](https://github.com/caunt/Void/commit/1a578e0ce44d6f19c6cc2afa129d7d4051b1050c))
* player-scoped events for scoped services ([f2f681b](https://github.com/caunt/Void/commit/f2f681b18a41bd618a8850dfa9aa50fe98be570a))
* stable hash codes for IPlayer ([32ec5ad](https://github.com/caunt/Void/commit/32ec5adb47841baeb2ad893c5793ce072c5cf4eb))
* stable hash codes for players ([68f6e32](https://github.com/caunt/Void/commit/68f6e32a5b2a7550e332309ba063cdbe3f08b5b1))
* update packet/message handling in extensions ([55f8ebf](https://github.com/caunt/Void/commit/55f8ebfbe3b22268991a805723912de8d5156abf))


### Bug Fixes

* add thread safety to MinecraftPacketIdPluginsRegistry ([f26c85b](https://github.com/caunt/Void/commit/f26c85bfe7da27145ba94fb912c1d9acd64d47cd))
* add using directive for extensions in EntryPoint ([0ab5b28](https://github.com/caunt/Void/commit/0ab5b289f72349485245078620d58fa8b5345f2a))
* allow creating composite without preferred container ([2bb05d7](https://github.com/caunt/Void/commit/2bb05d7c98d90c2d7d41b9531a589724d450a411))
* allow non-scoped events triggering scoped listeners ([da5ea30](https://github.com/caunt/Void/commit/da5ea305dfe144b660a67be35025a1ceb0008497))
* always register plugin services outside root container ([55d99c5](https://github.com/caunt/Void/commit/55d99c51cb7d1dc9ee5e9bdbea275543fd7b6e43))
* avoid collection modified exceptions in plugin transformers ([97d2430](https://github.com/caunt/Void/commit/97d243014ac25c1b2ad090ddb9c43d17da323948))
* clear plugin registries later ([5391e8e](https://github.com/caunt/Void/commit/5391e8e725e23270d44334f1b2ea030e9db90d25))
* composite scoped service providers for players ([ce35f90](https://github.com/caunt/Void/commit/ce35f90858668ecc400d157ac02c94bc9eebf30a))
* consolidate player context disposal and clean up code ([b6199e2](https://github.com/caunt/Void/commit/b6199e2b5508764602f1c7c0b4057d5e5d1d38d7))
* correctly dispose scoped player composite and do not dispose player instantly ([a4a7719](https://github.com/caunt/Void/commit/a4a7719d066bfd81e824bbb33d848200ee7664e9))
* dispose player services later ([ff9b2fa](https://github.com/caunt/Void/commit/ff9b2fad49d1481a97ad0aa0640fc663876e1137))
* double ioc container tracking ([81ffa92](https://github.com/caunt/Void/commit/81ffa92f187319a8011436c8e63cd902f459d673))
* escalate container user permissions ([de971ea](https://github.com/caunt/Void/commit/de971ea32d78e373a800bf88c736dc32b929610a))
* explicitly register services as listeners ([e63f81d](https://github.com/caunt/Void/commit/e63f81d60b5b20becc06a3b16bbb7004a98cb610))
* improve composite layers in player scoped resolutions ([bc2291d](https://github.com/caunt/Void/commit/bc2291d724140f7341f10366bfc27afcd5316bdc))
* improve error handling in UnloadContainerAsync ([81f28f1](https://github.com/caunt/Void/commit/81f28f15f36bc21a35d34656f83e2f03ac396f8b))
* improve event filtering and service registration logic ([b91ba76](https://github.com/caunt/Void/commit/b91ba767364b5ef4252b138b04e2cbc0d9409065))
* indexing typo in unloading configurations ([4b4bbc4](https://github.com/caunt/Void/commit/4b4bbc4a8b6520eabacf60cf9aeb3891781443d0))
* let other listeners process disconnected player before disposing him ([0d1a354](https://github.com/caunt/Void/commit/0d1a35450e3514401eea0865982dfe722e31e167))
* pass weak references to event listeners pipeline ([a38a5dc](https://github.com/caunt/Void/commit/a38a5dc745d57c4ec53e0db5acf0d1699b13d316))
* register services as event listeners on resolve ([0c7522d](https://github.com/caunt/Void/commit/0c7522d9b751f5e4575f2cc1ff92c8f8d5120cd4))
* remove cache clearing after service unregistration ([7c5f1b4](https://github.com/caunt/Void/commit/7c5f1b4ed64cb702f6157a7a67ec5a0aeb3d1b76))
* remove recursion from rethrowing events for new listeners ([a4dbb58](https://github.com/caunt/Void/commit/a4dbb58a54e72c2a0263f7b0791accd2325101cd))
* reset binary message stream position after packet id var int ([b83b15b](https://github.com/caunt/Void/commit/b83b15b4bb3ebf500a4a193431e3de2623c51ea8))
* resolve scoped services via composite ([f9ad174](https://github.com/caunt/Void/commit/f9ad1741563dc5a2c25a019c7847b3c948ca8a1b))
* save forwarded properties from previous player implementation to bypass ObjectDisposedException when trying to access RemoteEndPoint ([9aa0796](https://github.com/caunt/Void/commit/9aa0796e5f03039e3fca4dc0a91d1a2f47e23ffb))
* subscribe as event listeners services resolved from dependency service ([dbb91e6](https://github.com/caunt/Void/commit/dbb91e6141464160d25c81c39c8b29f2d7916974))
* unloading plugins with per-player scoped services ([740cd18](https://github.com/caunt/Void/commit/740cd18c4a8842651df3a38b7a1f669f07ab3f25))
* update dotnet.yml to use .NET 10 SDK preview ([67c6ef7](https://github.com/caunt/Void/commit/67c6ef7c4cb3ba984b5304587d430bd7fc4f8b15))
* update player in player context ([aa9b732](https://github.com/caunt/Void/commit/aa9b732204d2dd4d2bfda910fabdaa3fd91432f0))


### Performance Improvements

* optimize BlockSize in RecyclableMemoryStreamManager ([5a831f3](https://github.com/caunt/Void/commit/5a831f3f2c8d7f55075304d5dc254d8f4a449b0c))
* optimize byte array allocation in forwarding plugin ([32f64f5](https://github.com/caunt/Void/commit/32f64f507b9f3237fbcae8baccd2e5881a840ca4))

## [0.1.0](https://github.com/caunt/Void/compare/v0.0.2...v0.1.0) (2025-04-21)


### Features

* adapt settings to configurations service ([efce7a5](https://github.com/caunt/Void/commit/efce7a57d69d8273bfb15e8ad3c70c9bbb301f6f))
* add chat command event handling ([94b81b3](https://github.com/caunt/Void/commit/94b81b34254aa3b55a4b4d3ccea7ba83e5041586))
* add chat message for item slot change ([60a17ee](https://github.com/caunt/Void/commit/60a17ee8ff05c71150b03418f91779daf91efccd))
* add command registration functionality ([40e049b](https://github.com/caunt/Void/commit/40e049b5c0dd6e94cc46b9ce3ca3364647addc6e))
* add conditional logging levels based on build config ([2925285](https://github.com/caunt/Void/commit/2925285b0460b47976f00684198edaa6f8a9420c))
* add Containers property to IPluginService ([c082867](https://github.com/caunt/Void/commit/c0828671b747f3aa2ceae5aaecd5b2d704f2c1de))
* add dependency management service ([1489e09](https://github.com/caunt/Void/commit/1489e09c7d4b0dc2aa2dadc2a54afb9a175a0955))
* add embedded debug information to project file ([23a22b3](https://github.com/caunt/Void/commit/23a22b394e4744e71834e8d510fdbbe56eb22981))
* add experimental channel for newer Minecraft versions ([56f6adc](https://github.com/caunt/Void/commit/56f6adc4f98b0f9fdebcfc16f444460dc30a196b))
* add GetServer method to PlayerExtensions ([0066cf2](https://github.com/caunt/Void/commit/0066cf2eca43ebd23e868d1b181d93d78dd778b1))
* add handling for created entries in event service ([ad1cac4](https://github.com/caunt/Void/commit/ad1cac455c0cae2bda06e30178bfc88fabe13809))
* add IApiPlugin interface to plugins namespace ([0d10d67](https://github.com/caunt/Void/commit/0d10d6710f3f85135aa5ed2bcd79d2c9340f97b1))
* add ICommandDispatcher and ICommandNode interfaces ([30881da](https://github.com/caunt/Void/commit/30881da1e9c57ca30824f06c0d7b0d4957120473))
* add IDependencyService and refactor dependencies ([edb9362](https://github.com/caunt/Void/commit/edb9362ec395f26820b39e5cc1105b9545147465))
* add intercepting service provider classes ([1beed48](https://github.com/caunt/Void/commit/1beed480aaf315a89f3379f929b4f916a8626bc8))
* add logging and player retrieval enhancements ([da02773](https://github.com/caunt/Void/commit/da02773e25bc6c46b316f4c81b3d87b282a5c7e4))
* add logging for event listener registration ([c775de0](https://github.com/caunt/Void/commit/c775de0526d9edb49efd55370b545fd450c9cd67))
* add logging to command services ([7d6869e](https://github.com/caunt/Void/commit/7d6869e96b6c5777bc6446c031981bb9510121b9))
* add logging to DependencyService constructor ([77777bc](https://github.com/caunt/Void/commit/77777bcec6ca36cef4b62dc35cefef5621bfed62))
* add NewUuid method ([7cffe35](https://github.com/caunt/Void/commit/7cffe3583a3cc1206f13b7e3c3bf0c580535afef))
* add recursive method to traverse command nodes ([ef40beb](https://github.com/caunt/Void/commit/ef40beb29a71ee556c8f14e21307e568985d225e))
* add reflection extension methods and namespace ([a0886e5](https://github.com/caunt/Void/commit/a0886e592ae128d396854a8c5b08763e1f2a100c))
* add RegisterListeners method to HostingExtensions ([5aa82b7](https://github.com/caunt/Void/commit/5aa82b74e436cb499ddc3e0dafdbdcac92edfc1d))
* add RootConfigurationAttribute and improve file naming ([b02a079](https://github.com/caunt/Void/commit/b02a0793d5fe34fab54ac1ec3734690ae2ddf1b5))
* add service descriptor support methods ([a4677d9](https://github.com/caunt/Void/commit/a4677d953f94d8a988d9150e9c8044886f7a1442))
* add SetPropertyValue method ([d8adc49](https://github.com/caunt/Void/commit/d8adc49e34afd99fc4eb3821b9fdb33d01573e10))
* **chat:** enhance chat service with settings loading ([64aff83](https://github.com/caunt/Void/commit/64aff839959a80ddc494084a9ad111fbc081c93d))
* clear brigadier nodes from unloading plugins ([55cf9e0](https://github.com/caunt/Void/commit/55cf9e0ed88c70468e4b59ddd7f90829185c00b2))
* **dependency-service:** enhance IDependencyService interface ([1174011](https://github.com/caunt/Void/commit/1174011eabed924582c932c6d42aec6ce0368217))
* **dependency-service:** enhance service management functionality ([cb4bdd3](https://github.com/caunt/Void/commit/cb4bdd3f9c8c5deea611f3b4b7ff6608ac93b44d))
* **dependency-service:** enhance service retrieval methods ([f90e8e9](https://github.com/caunt/Void/commit/f90e8e90cb8cf063c2b0240f8e291683135c75e4))
* enhance chat command handling and player interactions ([a1877b8](https://github.com/caunt/Void/commit/a1877b8d90244403c2a97c81ef641307d42a649e))
* enhance chat event records with origin parameter ([7dfcf2a](https://github.com/caunt/Void/commit/7dfcf2aed4b07229ee9891f84bc0d5e09fd3485d))
* enhance command execution handling ([0858ec1](https://github.com/caunt/Void/commit/0858ec1c0de0815a3ddc780235f89caf1ce9533e))
* enhance command handling in services ([1453e3f](https://github.com/caunt/Void/commit/1453e3f2099189dcd0f5bc89f21087b58a3e39de))
* enhance command handling with interfaces ([48e5e2a](https://github.com/caunt/Void/commit/48e5e2a102c90ac42a44e3b972a9ac3e069376b2))
* enhance command message handling ([952d940](https://github.com/caunt/Void/commit/952d940bf06625d3bed04f4cb68b9b4f59a6a698))
* enhance command processing in CommandService ([63ef7d2](https://github.com/caunt/Void/commit/63ef7d21229645782b3a61caa04d708335351e6f))
* enhance ConfigurationTomlSerializer functionality ([3b3c2f7](https://github.com/caunt/Void/commit/3b3c2f74b483eec4a5ac05c66f677578332fcae4))
* enhance dependency injection and service registration ([9505414](https://github.com/caunt/Void/commit/9505414ed4102f525116ce04dadd4cd3938f2615))
* enhance dependency management in service classes ([143047c](https://github.com/caunt/Void/commit/143047c19a65f62bcd59f4206ff305d890a8e862))
* enhance dependency service instance creation ([d6aa321](https://github.com/caunt/Void/commit/d6aa321cb7e46ee78fb753d04fe3cbe9199eaf5a))
* enhance event handling in EventService ([7c91d6d](https://github.com/caunt/Void/commit/7c91d6dd047f9de9932a3a0859d87e766ae38a2c))
* enhance IDependencyService with activation control ([370e102](https://github.com/caunt/Void/commit/370e102d122f0e9d7c3819bcd8293d6deda5a97d))
* enhance IEventService with new methods and properties ([b17d758](https://github.com/caunt/Void/commit/b17d758d8852f6e7edf4cf68c0ee00c20402b4df))
* enhance InventoryService with command registration ([411a7f9](https://github.com/caunt/Void/commit/411a7f97a1dd2bb30d4863bf0dbfb9c6604ef1f6))
* enhance IPlayer interface with command sourcing ([3dad012](https://github.com/caunt/Void/commit/3dad0126cadcdfc6639670414561a657a6a44f71))
* enhance Link and LinkService with cancellation support ([c923171](https://github.com/caunt/Void/commit/c9231718e6923a9fa0b418495b084b086c336b50))
* enhance logging and service configuration ([1e499cb](https://github.com/caunt/Void/commit/1e499cb9642a1a21c1cacadf929555b72691ab80))
* enhance moderation commands in ModerationService ([5538910](https://github.com/caunt/Void/commit/55389106fcecc9e94693ad24da63929060b6b8f9))
* enhance moderation commands with suggestions ([4d25af9](https://github.com/caunt/Void/commit/4d25af940c3acf30556073f455c7cf565775479e))
* enhance PlatformService with command handling ([1044dab](https://github.com/caunt/Void/commit/1044dab69f5fab95b63ad3b284fec21eb659b347))
* enhance PlatformService with container commands ([24d7d8e](https://github.com/caunt/Void/commit/24d7d8ecbc3a90c5cc8a6e627087b1d5d744c25d))
* enhance plugin management with container support ([f84e44c](https://github.com/caunt/Void/commit/f84e44ca27bb275b0cb3e646ddb40005ecbce127))
* enhance RedirectionService with command handling ([36098cc](https://github.com/caunt/Void/commit/36098cc74374a27578e7c18ac92ca35ab48985b1))
* enhance service management and property handling ([0476ecb](https://github.com/caunt/Void/commit/0476ecbaa4af06fdcd7e582176c2ac34c04347d2))
* enhance service management in HostingExtensions ([5631775](https://github.com/caunt/Void/commit/56317750e3677576c00ea7d63608c0b29f2f3422))
* enhance service provider validation ([37d6481](https://github.com/caunt/Void/commit/37d6481c2fc8c4c4d16937db16ff6296e2dbc64f))
* enhance Uuid struct with comparison and equality ([c00b9e5](https://github.com/caunt/Void/commit/c00b9e5d9cae3ce83a5b1411110bd7fcc280efba))
* enhance WeakPluginContainer for better management ([ee796e3](https://github.com/caunt/Void/commit/ee796e361868d9bd7b81c4fef145473db5cfbd0f))
* **event:** add RegisterListener&lt;T&gt; method and duplicate check ([f6e7426](https://github.com/caunt/Void/commit/f6e74269d14d5a4aa2f266cc0ac7440992f17a77))
* **ExamplePlugin:** enhance service registration ([3257642](https://github.com/caunt/Void/commit/3257642f7f621ff5e9ab8dda9944ed2842a06002))
* **formatting:** set default formatting properties to false ([35ed244](https://github.com/caunt/Void/commit/35ed2443baeadb00859e8bd0eecd734a66881ebc))
* improve argument retrieval in CommandContext ([55d8d2f](https://github.com/caunt/Void/commit/55d8d2f9569b830513013a9825f1a325ec071ee8))
* improve segment handling in ComponentLegacySerializer ([42a680c](https://github.com/caunt/Void/commit/42a680cf3567231a9cc7dc12674a3ca4bc2f7e95))
* improve service instance creation and retrieval ([ce1295a](https://github.com/caunt/Void/commit/ce1295ae540abc157135a039d997570cff636274))
* instantiate example services from hosting abstractions, effectively using automatic registration of them as event listeners ([a6aa49e](https://github.com/caunt/Void/commit/a6aa49e5d8317eec81844d0d8290124e60dc2afc))
* integrate DryIoc for dependency injection ([4e7972d](https://github.com/caunt/Void/commit/4e7972dc8c9d808d93f76e04037e54b7a6375fd8))
* introduce IDependencyService for better dependency management ([a543fea](https://github.com/caunt/Void/commit/a543fea26ade2abb558c8e2484722d3f50876389))
* make IDependencyService inherit IServiceProvider ([72fcd2b](https://github.com/caunt/Void/commit/72fcd2b0dbe8d06d14a52f8306cb2a6e8bbd0bf3))
* move custom suggestions property to ArgumentCommandNode ([0d9aa60](https://github.com/caunt/Void/commit/0d9aa60e7bf91a657b00808d7711a1f876a607e6))
* move to dry ioc container ([62a1800](https://github.com/caunt/Void/commit/62a1800a10927672c935a9a3a8bc5c88ef3e976c))
* per-assembly containers ([856d2e0](https://github.com/caunt/Void/commit/856d2e0e8b4a895586289c62e2d5a4c2a568f90e))
* per-player scoped services ([543bece](https://github.com/caunt/Void/commit/543becea9423bfca126686b2beabd6b824535cc4))
* **plugin:** enhance dependency loading mechanism ([f7ef966](https://github.com/caunt/Void/commit/f7ef9666c09807252d3ba529190399d125872ab1))
* **plugins:** add PluginExtensions for improved plugin retrieval ([e5150a5](https://github.com/caunt/Void/commit/e5150a5b601aaed66850d15dcae8fb64f0cd0ae2))
* register listeners in service configuration ([270b31b](https://github.com/caunt/Void/commit/270b31be5784a30ed1503bbc5b6d8a12519a1370))
* register text component transformations 1.21.4 =&gt; 1.21.5 ([bca74f6](https://github.com/caunt/Void/commit/bca74f638bf29df7774045da616e5b4190c4a9d4))
* rename constructor parameter in PluginAssemblyLoadContext ([2ec4f39](https://github.com/caunt/Void/commit/2ec4f399ac999cf3eeab59542e18b61f005cd8ce))
* replace service provider factory with DryIoc container ([5bd218d](https://github.com/caunt/Void/commit/5bd218d67a65932562491637e4498b8ce8bd2bf3))
* simplify ConfigurationService constructor ([9b36f10](https://github.com/caunt/Void/commit/9b36f10dac40cdc31a476581e107c269b3674f1e))
* simplify IServiceProvider.Add method ([b7fc0f7](https://github.com/caunt/Void/commit/b7fc0f7e03a4539a63c5e0f1654f53a25f2fb50a))
* split loading/loaded and unloading/unloaded plugin events ([6748323](https://github.com/caunt/Void/commit/67483238c58b58fb8b76505b80656eb92017c555))
* streamline command registration for "kick" ([45b2536](https://github.com/caunt/Void/commit/45b253672ea798651760f51c3f5d555c02c2d7c0))
* synchronize plugin events ([cbae8a2](https://github.com/caunt/Void/commit/cbae8a2b3ba7eb049b2181f19567d995ef796a3f))
* text component transformations 1.21.4 =&gt; 1.21.5 ([35c478f](https://github.com/caunt/Void/commit/35c478f9aab5f3cd0efd6cc138340e6567a1bc03))
* **trace:** add TraceService for message logging ([0a4118a](https://github.com/caunt/Void/commit/0a4118a7bddab1d7d7f8e335a564ff48b1bef7ea))
* update command handling and logging in AbstractCommandService ([304c38f](https://github.com/caunt/Void/commit/304c38fc46aa8cf3219b11c6da37e945c530200c))
* update CommandService for chat message handling ([0f9b7d7](https://github.com/caunt/Void/commit/0f9b7d759aee514a3ac94a4d8d1ba7ccf8c8c57d))
* update configuration file to new configuration service ([6c51c0d](https://github.com/caunt/Void/commit/6c51c0d7f056229c058022ed8d81599b494295f3))
* update dependency injection and service management ([de3f18a](https://github.com/caunt/Void/commit/de3f18a5e521275823eefcd759469c328961084e))
* update ExamplePlugin constructor for better logging ([7079149](https://github.com/caunt/Void/commit/7079149a7caad88f0a78fdf5705f47d27d78cd02))
* update IClickEventAction records with parameters ([0cee75f](https://github.com/caunt/Void/commit/0cee75fe165ffb0c9ff164364e5e2dd9a7947124))
* update ModerationService and PlatformService ([f2f7d6d](https://github.com/caunt/Void/commit/f2f7d6d43f4a67d34d8128377659fc34c0440093))
* update player link retrieval methods ([9df2fae](https://github.com/caunt/Void/commit/9df2fae88843537a412ceaff76400baaff94816f))
* update Plugin class to use dependency injection ([8d87944](https://github.com/caunt/Void/commit/8d8794459fe393c5ebe1bcb77bd38be7e82c71d8))
* update Property record constructor and remove Deconstruct ([09c17fb](https://github.com/caunt/Void/commit/09c17fb619070c93624fc8bd321855ca9996a445))
* update service retrieval in DependencyService ([de1ae86](https://github.com/caunt/Void/commit/de1ae860a55a368efb149f9643de975d7519d9f1))
* update StringRange to use range operator ([ab90cd4](https://github.com/caunt/Void/commit/ab90cd40eac6bfc22512e1041448e7d5b0b7b83d))
* update text components to 1.21.5 ([37945d0](https://github.com/caunt/Void/commit/37945d04b2694d4619f6548ccf17b7a5197bea50))


### Bug Fixes

* activate only singletones ([54994ba](https://github.com/caunt/Void/commit/54994babbf7ed03b295a7bb8ee0cfe5a487feea8))
* add error handling in SimpleNetworkStream ([b1b17a1](https://github.com/caunt/Void/commit/b1b17a1a839a914dfe35c25074287c769a23a5b6))
* allow example plugin unloading ([b298690](https://github.com/caunt/Void/commit/b2986906a9a1aa8f5dbef143e53b41bfcfa9b6a1))
* clear event listeners registered via dependency service ([feba68e](https://github.com/caunt/Void/commit/feba68e44c4097545c84f0b3586719c69666d409))
* correct method name for encryption response packet ([061d4bc](https://github.com/caunt/Void/commit/061d4bc77ae99ea45cf3e6dcc1e6a88532d790c8))
* correct typo in README.md ([d5d39e9](https://github.com/caunt/Void/commit/d5d39e9efdf12aa680d311fffcd2cb584a17f841))
* do not forward commands if they are coming from player and are forwarded ([2871111](https://github.com/caunt/Void/commit/28711119eb73a6d289b3d9ce297de334c5624b53))
* ensure thread safety in ConfigurationService ([5020154](https://github.com/caunt/Void/commit/5020154c632e4e8e9ab0636e8059e7085278ebc0))
* filter only outgoing chat messages ([2103152](https://github.com/caunt/Void/commit/2103152588feed1fb05164842d16b19a9f7ac98f))
* handle empty tag names in NbtReader ([6eabfd1](https://github.com/caunt/Void/commit/6eabfd1416b89c9d0e99a823d1f83eab8a66c900))
* improve plugin file retrieval order ([9738607](https://github.com/caunt/Void/commit/9738607805bf531ab7d06a5ac48e6b2c078e01fc))
* improve plugin service management in DependencyService ([08220ca](https://github.com/caunt/Void/commit/08220ca8e10ebc6b370763c401e706194c374283))
* improve service instance creation in DependencyService ([94af648](https://github.com/caunt/Void/commit/94af648ef93c312faad299529e8a18ccb1b9947e))
* improve service management in DependencyService ([99eb539](https://github.com/caunt/Void/commit/99eb539a109962b5218e50ee811b2aadb8a5fccd))
* improve substring extraction and readability ([94edd62](https://github.com/caunt/Void/commit/94edd62d0363bfcac51ecb102b4a9c8fe62ff090))
* improve substring handling in CommandSyntaxException ([d635c21](https://github.com/caunt/Void/commit/d635c2185e9ef9e46bc20c1666c73be81962326f))
* pass serilog factory into a child containers ([7668170](https://github.com/caunt/Void/commit/7668170d9a51d598ee4c99c19021dd9e3f87dc95))
* remove deprecated service removal from players scope ([427414f](https://github.com/caunt/Void/commit/427414faf81dcec40e018bbee5b67b81480c84b8))
* remove intercepting service provider ([5380b2a](https://github.com/caunt/Void/commit/5380b2a53a8388130728f64fcd1cb850f1ffbbc3))
* reorder method calls for plugin file processing ([098ee8b](https://github.com/caunt/Void/commit/098ee8bb0334bb86048e252d4f53e994ecd25cda))
* simplify EventService constructor and cancellation handling ([6873af2](https://github.com/caunt/Void/commit/6873af2239d0a0dcb1be23710b6acefedf44e843))
* simplify OnPluginUnload method in DependencyService ([1b164a1](https://github.com/caunt/Void/commit/1b164a112a8232da1bca59928230dee9a4dbeae3))
* streamline instance creation in DependencyService ([5e630c4](https://github.com/caunt/Void/commit/5e630c4e3904fcdc14eee69c197f59861ee5939a))
* uncomment previuos unloading problem ([3a11604](https://github.com/caunt/Void/commit/3a11604fcf2dce09bcefb3984b9dd80dcbc04e37))
* update exception messages for registry management ([580e2bd](https://github.com/caunt/Void/commit/580e2bddd83d24d0f10090949a3e303ce6bfeb4f))
* update instance creation logic in DependencyService ([e20e38c](https://github.com/caunt/Void/commit/e20e38c94a6351085582d033c7222b215cd0e159))
* update mapping version comparison logic ([62ee5bb](https://github.com/caunt/Void/commit/62ee5bb04c79051bca49b9d4684715352a5910e3))
* update service access in DependencyService ([39f8c2c](https://github.com/caunt/Void/commit/39f8c2c54c0b45a4a28f311d37070cda8cd609ee))

## [0.0.2](https://github.com/caunt/Void/compare/v0.0.2...v0.0.2) (2025-04-14)


### Features

* **AbstractRegistryService:** enhance stream handling ([7835c99](https://github.com/caunt/Void/commit/7835c996e0b8e740c0275a030025d1dbf9cfcecd))
* **actions:** prerelease versioning ([941f0bd](https://github.com/caunt/Void/commit/941f0bd8807a1b618440599484af841c46010774))
* **actions:** test update ([55cc80d](https://github.com/caunt/Void/commit/55cc80d0f138244ed0cd954344f15ca387f4ea23))
* **actions:** test update ([4bd2095](https://github.com/caunt/Void/commit/4bd2095668366020fde05bb790b3cfdd05cd5aea))
* **actions:** test update ([ef4bd71](https://github.com/caunt/Void/commit/ef4bd714580ecdfe64154c1a229c2a0fde3e1f32))
* **actions:** test update ([09063b5](https://github.com/caunt/Void/commit/09063b54b63df28135dfd29ceaac1f7f25a8ce10))
* **actions:** test update ([5c7ce73](https://github.com/caunt/Void/commit/5c7ce732414f2b05b0216c32e0ab41e5dd7b6847))
* **attributes:** add configuration attributes and improve serialization ([477a3b1](https://github.com/caunt/Void/commit/477a3b1e46ab0920a91280b71c887e09e7613b9a))
* **authentication:** enhance message handling and packet limit ([13b6839](https://github.com/caunt/Void/commit/13b6839b6d8608397dfad6efa39f0cc18f994f64))
* **authentication:** improve server compatibility for players ([bb77b25](https://github.com/caunt/Void/commit/bb77b25d39187926a0fce81d150ea9436e3280aa))
* **authentication:** update protocol version handling ([5bf2858](https://github.com/caunt/Void/commit/5bf285846a4853a811c1f0187767da9335d430fd))
* base transformation property types ([#3](https://github.com/caunt/Void/issues/3)) ([9b84973](https://github.com/caunt/Void/commit/9b849737ca44be7620e54fbf6dd8d1a8aeb82fc6))
* **buffer:** implement IMinecraftBuffer interface and extensions ([30aef7c](https://github.com/caunt/Void/commit/30aef7c8cf1f0b04a45089f9ddda6e3fd90516b8))
* **buffer:** introduce BufferMemory and BufferSpan structs ([fc781b7](https://github.com/caunt/Void/commit/fc781b7aaee85a51c4e2e5f23a00a804db418da7))
* **buffers:** add BufferStream struct in new namespace ([db12623](https://github.com/caunt/Void/commit/db1262329641be390bb3ecfafd0d2af0eb2a588f))
* **buffers:** enhance reading and writing methods ([2ecac77](https://github.com/caunt/Void/commit/2ecac7774127fba2e0fb72e390ea711d7c4ed737))
* **ChatMessagePacket:** update message handling to use string ([0fc01e3](https://github.com/caunt/Void/commit/0fc01e39997d0744645f0581148a48ae7fee1664))
* **chat:** update ChatMessagePacket for protocol changes ([8aba07f](https://github.com/caunt/Void/commit/8aba07fdd3a8534d1700e5e6e0a1e2a8e7464908))
* **colors:** add color downsampling and serializer updates ([96e0b65](https://github.com/caunt/Void/commit/96e0b65a739575e5f95345ed37870b2da2f5572e))
* **common:** restructure networking interfaces and add features ([38f732f](https://github.com/caunt/Void/commit/38f732f4434543cdfd92898ec7a894451d661a64))
* **component:** add AsText property and ReadFrom method ([18f8400](https://github.com/caunt/Void/commit/18f8400951a282e9e4cdf93469a72a8787aa0742))
* **component:** add buffer serialization methods ([663474b](https://github.com/caunt/Void/commit/663474bb5486cec9880113dfb925b8794138603c))
* **component:** enhance serialization methods and documentation ([6cdd9c4](https://github.com/caunt/Void/commit/6cdd9c482bd0c76da9c92b590a90302d236eb580))
* **config:** add async methods for configuration retrieval ([6590cb7](https://github.com/caunt/Void/commit/6590cb7782196d90eba06d1f9923902253a5d25f))
* **config:** add IConfiguration and IConfigurationService interfaces ([a31c7a3](https://github.com/caunt/Void/commit/a31c7a3fe77748c5dcf71a87967deed06da96775))
* **config:** add plugin support to IConfigurationService ([978e99f](https://github.com/caunt/Void/commit/978e99f047567905d74b49fa86f4d74d393ac4ba))
* **config:** enhance IConfigurationService with event handling ([85dace4](https://github.com/caunt/Void/commit/85dace4ff826567de33494a7b2e2e2ff7118492c))
* **config:** refactor configuration handling and loading ([e549bf2](https://github.com/caunt/Void/commit/e549bf28b5ca92cebe75aeebb347eb72f581b7bb))
* **configuration:** enhance serialization and type handling ([f16f385](https://github.com/caunt/Void/commit/f16f385276e038a8535bc2418e6555dd6dd10d6c))
* **configuration:** implement timer-based monitoring ([872c948](https://github.com/caunt/Void/commit/872c9481c2f2c812250c0a960884a96b8a86a7de))
* **configurations:** add ConfigurationNameAttribute class ([f455771](https://github.com/caunt/Void/commit/f4557710e9839692c1e4a415d5508d329809ad8c))
* **configurations:** add ConfigurationService class ([9b1e73b](https://github.com/caunt/Void/commit/9b1e73b63d2812db3939a789ff957ed4cf72b76e))
* **ConfigurationService:** add skippedUpdates for efficiency ([5572de6](https://github.com/caunt/Void/commit/5572de6121eec4b39aedc887c3d24d70699223d6))
* **ConfigurationService:** enhance logging and file watcher ([0270618](https://github.com/caunt/Void/commit/0270618bd5ae4eb7bff139cc2c254a39c7afccf1))
* **ConfigurationTomlSerializer:** enhance TOML mapping and swapping ([aae43b6](https://github.com/caunt/Void/commit/aae43b6ca4c06a390d3d3fd7cef65f5d41c41659))
* **deps:** add TypeExtender package reference ([93e027f](https://github.com/caunt/Void/commit/93e027f81c2eddd10ba60ac084456aab30a25b8f))
* **docker:** update container message and add env variable ([981b9c5](https://github.com/caunt/Void/commit/981b9c51fbe2aa954b750f14f79d20130265b269))
* **docker:** update imageTag logic for Minecraft versions ([d1162fd](https://github.com/caunt/Void/commit/d1162fd42d9576db76f89f258ec77a90c92035ad))
* **docker:** update Minecraft server version and type ([1630312](https://github.com/caunt/Void/commit/16303120eedd36a6d7840175073e9d2c36ba0235))
* **editorconfig:** enforce newline at end of files ([cabc1bf](https://github.com/caunt/Void/commit/cabc1bf80faa029857ac8b4b2cf65a504c84d3d5))
* enhance Minecraft protocol handling and transformations ([9f4154c](https://github.com/caunt/Void/commit/9f4154cb2bbb0d2c50c1d324635a79787b89b3f9))
* **entrypoint:** add configuration service support ([4e9bb2a](https://github.com/caunt/Void/commit/4e9bb2a2938104acede4a77193d189907ebdf573))
* **EventService:** enhance cancellable parameters creation ([be5d2ac](https://github.com/caunt/Void/commit/be5d2ac9974a3f2d2acbf2ee5a2398f4120a00c1))
* **EventService:** enhance event handling with lifecycle support ([7fb8de0](https://github.com/caunt/Void/commit/7fb8de0ee47ef7b10be8f717ee1bca8a650a2650))
* **EventService:** improve parameter type compatibility check ([f486532](https://github.com/caunt/Void/commit/f4865320f5f1967f9bc5bd101f9d0e892ae3873a))
* **exceptions:** add InvalidConfigurationException class ([b4fd0ee](https://github.com/caunt/Void/commit/b4fd0ee5fce67bf0a30792a15f1bebf5ef565c48))
* **exceptions:** introduce StreamClosedException for stream handling ([c744b86](https://github.com/caunt/Void/commit/c744b86fc73cc540c7182516e61d2663c893289b))
* **extensions:** add IntExtensions class for VarInt handling ([fdf75cc](https://github.com/caunt/Void/commit/fdf75ccbebf81e90f6abd0917fcd9309f3531eb6))
* force named nbt in named nbt property ([a977ea4](https://github.com/caunt/Void/commit/a977ea4b5426e8927748057acaaa84f008d0a4ce))
* **interface:** add new methods and property to IMinecraftBinaryPacketWrapper ([81ab6c3](https://github.com/caunt/Void/commit/81ab6c30ee55f57df9d1ccee0dbd88e847435d48))
* **IntExtensions:** add EnumerateVarInt method ([da64c74](https://github.com/caunt/Void/commit/da64c74be94929d417e7f75e9ed78b646b0de202))
* **lifecycle:** add checks for player and protocol version ([5033146](https://github.com/caunt/Void/commit/50331462e2b9e6cfc2503d769bd2d43540ada5e6))
* **link:** add destructor and improve lock usage ([84da14b](https://github.com/caunt/Void/commit/84da14ba932a5ec4212d11d9a6292be3d745d710))
* **link:** enhance task management during stopping process ([91e45d0](https://github.com/caunt/Void/commit/91e45d0d38a558aa96e0e2b337f6045ce4bd7b25))
* **logging, packet:** update logging level and enhance dimension data handling ([a0565a3](https://github.com/caunt/Void/commit/a0565a36bf2525bda6013380c9c3645ecf06a351))
* **logging:** enhance error handling and connection logging ([a77b9d5](https://github.com/caunt/Void/commit/a77b9d51f32c5a287806d61d755dbd0afb38759d))
* **message-handling:** refactor to use INetworkMessage interface ([83de631](https://github.com/caunt/Void/commit/83de631c890f2b6ba7245633eeb99e495bc055f7))
* **MessageReceivedEvent:** add cancellation support ([b98515d](https://github.com/caunt/Void/commit/b98515d807288c5af04251b26404c9f2f2b38a31))
* **MinecraftBuffer:** add Dump method for buffer snapshot ([14713b8](https://github.com/caunt/Void/commit/14713b8e82bb96973a3d2044ea706f4e334f3a00))
* **minecraft:** update protocol version and Docker setup ([a37a88e](https://github.com/caunt/Void/commit/a37a88e689c56b753e527b63756461a29ccdb2a8))
* **minecraft:** update protocol version and refactor method call ([5280bf6](https://github.com/caunt/Void/commit/5280bf6e170cb0cb5948a593dbb7d6b4f388d276))
* named nbt tags ([82219fc](https://github.com/caunt/Void/commit/82219fcffec3420d51f6245edad838d89d3e153f))
* **namespace:** refactor and modularize network structure ([45356b5](https://github.com/caunt/Void/commit/45356b5c6064e7adf7a430561b86b2c4b6e1fd82))
* **nbt:** add implicit conversions between NbtBoolean and NbtByte ([06a65bc](https://github.com/caunt/Void/commit/06a65bc3634c3c5daa528ae7de14cedd5662f8b5))
* **nbt:** add implicit conversions for NbtBoolean ([28216ae](https://github.com/caunt/Void/commit/28216aec7300e82b9097bc95a57cc7d7cc7ad4c8))
* **nbt:** add NamedNbtProperty and enhance NbtProperty ([cfe3fc2](https://github.com/caunt/Void/commit/cfe3fc2b6e502df268d1e514064b6600556e765e))
* **nbt:** enhance NbtTag with buffer reading capabilities ([287b277](https://github.com/caunt/Void/commit/287b277bea64ea0c5a36a5e72fa2e86f3b947a37))
* **NbtTag:** refactor AsStream and AsJsonNode methods ([6e5c39c](https://github.com/caunt/Void/commit/6e5c39cc9b02b0a822e76e6e78c2f18eba908625))
* **network:** add Side parameter to message handling ([100a2cb](https://github.com/caunt/Void/commit/100a2cbdd3a9e9495686fbd6a47f5f6bde7b76c3))
* **network:** refactor namespaces and add new interfaces ([3cdf567](https://github.com/caunt/Void/commit/3cdf567b12b08184597a211a9e16acd673f0e108))
* offile uuid support ([c3203e5](https://github.com/caunt/Void/commit/c3203e5ae65ed0c4dfa533da75861eb539917fb1))
* **packet:** add Checksum property to SignedChatCommandPacket ([e720547](https://github.com/caunt/Void/commit/e7205478c8df1162fca592fd36105af51dcdab44))
* **packet:** add constructor for ChatMessagePacket v1.20.2-3 ([a616a81](https://github.com/caunt/Void/commit/a616a81fb855c45ef770e39fc20c480c824448e1))
* **packet:** add transformation mappings for ChatMessagePacket ([aee53b1](https://github.com/caunt/Void/commit/aee53b1fbeadfaac05a341bb073b0a81204c948d))
* **packet:** update reducedDebugInfo handling ([3a83a4f](https://github.com/caunt/Void/commit/3a83a4f6e2906f3466d2caa6b2a1e69b3fc51407))
* **player-service:** add Minecraft player functionality ([4d219c4](https://github.com/caunt/Void/commit/4d219c49cca613c6b98fe2d15199f698f6fd3625))
* **plugins:** add ignoreEmpty parameter to LoadPluginsAsync ([9937b69](https://github.com/caunt/Void/commit/9937b690204629100bfdca01b41aa566f0f290c0))
* **plugins:** refactor plugin interfaces and services ([6ea6420](https://github.com/caunt/Void/commit/6ea6420512235a7dea1149b6524134221a94f80c))
* **program:** enhance version handling in Docker setup ([e141aeb](https://github.com/caunt/Void/commit/e141aebb6510e6869144219123dadabe34783e76))
* **program:** enhance version retrieval in Docker setup ([f3e6bee](https://github.com/caunt/Void/commit/f3e6bee745eb7d9c9cff19019fcaa4fa3087ba61))
* **program:** update default protocol version and add type ([1bae35c](https://github.com/caunt/Void/commit/1bae35c25674983fa166cb5f5a0ead52552ab5f8))
* **program:** update Minecraft version handling ([557d2d1](https://github.com/caunt/Void/commit/557d2d1dfe730b1b3bf59abe6eb42891d074b40a))
* **properties:** add BinaryProperty for handling byte data in packets ([db491f7](https://github.com/caunt/Void/commit/db491f7f3633ca5a5604b303d4f355596c49a0f4))
* **protocol:** add transformations for v1.7.6 to v1.8 ([26cf6e2](https://github.com/caunt/Void/commit/26cf6e297c5194520de4d22848bf0226c90e6820))
* **protocol:** correct version number for Minecraft 1.9.4 ([3092055](https://github.com/caunt/Void/commit/30920558cd6b89f6027a1d80f2689a00c86943f7))
* **protocol:** update chat message handling for new versions ([25cf813](https://github.com/caunt/Void/commit/25cf813983e2887a3a3911827790abb9e5ac851f))
* **protocol:** update Minecraft protocol to 1.11.1 ([9720468](https://github.com/caunt/Void/commit/972046839ffbeee234e1577c4484e878ff2c5d92))
* **protocol:** update Minecraft protocol version to 1.21 ([ec084d6](https://github.com/caunt/Void/commit/ec084d6eba4293d08beeef0c56cd3226506c6bf2))
* **protocol:** update Minecraft protocol versions and transformations ([8b811ae](https://github.com/caunt/Void/commit/8b811aefbbe8fece775a14888bd421e06504c4a8))
* **protocol:** update to latest Minecraft protocol version ([e57d9c0](https://github.com/caunt/Void/commit/e57d9c0339aff7e0deb8ba5d92f6e7062cf12e38))
* **protocol:** update to Minecraft 1.21.5 support ([63ab8a6](https://github.com/caunt/Void/commit/63ab8a6ec7770bc0d83ca174e05bedcb756b1cbf))
* **protocol:** update version check for chat message packet ([216f08e](https://github.com/caunt/Void/commit/216f08ebe364270f12dcdbebd038b683649ffb7a))
* read/write short ([93b70bc](https://github.com/caunt/Void/commit/93b70bcab0a1a86f648d46fa7a82f00a5399a69f))
* register transformators ([729e5af](https://github.com/caunt/Void/commit/729e5af3d105f348e500a0ec558b98f09ce2d557))
* **registry:** add IRegistryHolder interface and implementation ([84636a0](https://github.com/caunt/Void/commit/84636a086a568ec4b4eb35863fb85824fb92df95))
* **Registry:** add new chat message packet mappings ([97ff2a6](https://github.com/caunt/Void/commit/97ff2a6148a7fe2eb29a604f15d3221b5a20951e))
* **registry:** implement IRegistryHolder and update error messages ([aec211e](https://github.com/caunt/Void/commit/aec211e2d25f76fcaa7ffb38470e93815fc47f43))
* **serializer:** add IConfigurationSerializer and TOML support ([a4e0218](https://github.com/caunt/Void/commit/a4e021849c5450f7878e491dd1389985c8a65bcf))
* **serializer:** add JSON deserialization for Component ([6434b15](https://github.com/caunt/Void/commit/6434b1565e82ad72794fa9f40b5bf8288d55f5d1))
* **serializer:** add methods for object serialization ([e74d5cc](https://github.com/caunt/Void/commit/e74d5ccfee5c62e9b0c945ef8696121d52190101))
* **serializer:** add overloads for Serialize method ([19bde35](https://github.com/caunt/Void/commit/19bde35b019c347dfd5816385098f1a279876c51))
* **serializer:** enhance attribute handling in ConfigurationTomlSerializer ([4ac3419](https://github.com/caunt/Void/commit/4ac3419f503618edd67d92f1c37e73f18a88372a))
* **serializer:** enhance configuration serialization methods ([33d3b71](https://github.com/caunt/Void/commit/33d3b71dbd7474b9cdfc33b62cb75fa51cdbdac4))
* **serializer:** enhance dynamic type generation ([7ea423c](https://github.com/caunt/Void/commit/7ea423cb0c34355eed4c11f9e314dbf7735f42fe))
* **service:** enhance IConfigurationService for background tasks ([e9ee168](https://github.com/caunt/Void/commit/e9ee16824391e81f8e0e9fbb7f1ca0c8f98dfd19))
* **services:** add hosted services for Configuration and Platform ([d7f2ca4](https://github.com/caunt/Void/commit/d7f2ca4e2c38ea425e5063603944e93e697f8d40))
* **ShowEntity:** update parameter order and add ActionName ([123e7ae](https://github.com/caunt/Void/commit/123e7ae26c031223d9ac95ca72085998443fae59))
* **streams:** refactor to introduce IManualStream interface ([ff6aff5](https://github.com/caunt/Void/commit/ff6aff5514cb289b4bfdb553505ba8038aced0f3))
* **transformation:** replace NbtProperty with ComponentProperty ([49a3e80](https://github.com/caunt/Void/commit/49a3e8042c0f21b64cc09787411f37284ec74ca9))
* **transformations:** add packet transformation functionality ([1760199](https://github.com/caunt/Void/commit/1760199531e42d71f8f5c4c1db94efd26643f20e))
* **transformers:** add methods for version upgrades and downgrades ([267f59a](https://github.com/caunt/Void/commit/267f59a1ba562129d9c87b5e53206cf5309f747e))
* **transformers:** add version handling for JSON and NBT ([80e0c5d](https://github.com/caunt/Void/commit/80e0c5d1dd4ba9ebb54709cbda94fdaa1152a16d))
* **transformers:** add version passthrough methods ([8eee759](https://github.com/caunt/Void/commit/8eee759ad8857e9c94a7aa77d9d8c2400b24be9a))
* **transformers:** enhance Apply method for protocol versions ([e17a0ec](https://github.com/caunt/Void/commit/e17a0ec97ea59772636f845a4bca66e58d03e87f))
* **transformers:** enhance hoverEvent handling ([6c8d111](https://github.com/caunt/Void/commit/6c8d111e819478161a726fa5e21c0d8697984af3))
* **transformers:** enhance JSON and NBT component handling ([2a2cf6a](https://github.com/caunt/Void/commit/2a2cf6a707d4bea52aed958c720c7d1d7ea60835))
* **versioning:** target specific Minecraft version 1.21 ([6d1cace](https://github.com/caunt/Void/commit/6d1cace9b1ccef14c6934739a4223303200be90e))
* wrap packets on send ([750b01c](https://github.com/caunt/Void/commit/750b01c8109a650fd0cc0507cd60525e5b3541e2))


### Bug Fixes

* **actions:** syntax error ([04730ee](https://github.com/caunt/Void/commit/04730ee763ae13c97de82660e4efcc91fb670d94))
* **ChatMessagePacket:** make Sender property non-nullable ([c437042](https://github.com/caunt/Void/commit/c437042103d65c8f66b4402d446d9615639f9d7b))
* correct return statement in AsVarInt method ([c1d65b6](https://github.com/caunt/Void/commit/c1d65b648ce40b01519614029ca90a44e1109c6f))
* **link:** replace cancellationToken with forceCancellationToken ([48f9c37](https://github.com/caunt/Void/commit/48f9c376d701c3ac0caa6c63e8be435e39cd3f5e))
* **PlayerService:** update exception handling logic ([c5b58ce](https://github.com/caunt/Void/commit/c5b58cec826764c5092aa880e7410248494a9257))


### Performance Improvements

* optimize VarInt writing in buffers ([348767d](https://github.com/caunt/Void/commit/348767d76f34c2797c6d803b0355c6c0584584e9))
