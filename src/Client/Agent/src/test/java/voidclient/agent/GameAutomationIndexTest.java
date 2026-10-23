package voidclient.agent;

import java.io.InputStream;
import java.lang.reflect.Field;
import java.net.URI;
import java.net.URL;
import java.nio.file.FileSystem;
import java.nio.file.FileSystems;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.CodeSource;
import java.security.ProtectionDomain;
import java.security.cert.Certificate;
import java.util.Collections;
import java.util.Map;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.tree.ClassNode;

public final class GameAutomationIndexTest {
    @Test
    public void readsGameCodeServedFromAnInstalledFileSystem() throws Exception {
        // NeoForge 1.21.1 serves Minecraft classes from union: file systems; a zip file system stands in for one here.
        String name = GameAutomationIndexTest.class.getName().replace('.', '/');
        Path jar = Files.createTempFile("void-client-index", ".jar");
        Files.delete(jar);

        try (FileSystem fileSystem = FileSystems.newFileSystem(URI.create("jar:" + jar.toUri()), Collections.singletonMap("create", "true"));
             InputStream input = GameAutomationIndexTest.class.getResourceAsStream("/" + name + ".class")) {
            Path type = fileSystem.getPath(name + ".class");
            Files.createDirectories(type.getParent());
            Files.copy(input, type);

            Map<String, ClassNode> types = GameAutomationIndex.readTypes(URI.create("jar:" + jar.toUri() + "!/"));

            Assert.assertEquals(Collections.singleton(name), types.keySet());
        } finally {
            Files.deleteIfExists(jar);
        }
    }

    @Test
    public void skipsJdkModulesAndLocationsWithoutAnOpenFileSystem() throws Exception {
        Assert.assertNull(GameAutomationIndex.readTypes(URI.create("jrt:/java.sql")));
        Assert.assertNull(GameAutomationIndex.readTypes(URI.create("jar:file:/missing/void-client.jar!/")));
    }

    @Test
    public void jdkClassesNeverWaitForAThreadThatIndexesGameCode() throws Exception {
        // The JDK loads its own classes while it holds locks that reading a jar needs too, which deadlocked games that were indexing.
        final ProtectionDomain jdkModule = new ProtectionDomain(new CodeSource(new URL("jrt:/java.sql"), (Certificate[]) null), null);
        final Object[] indexes = { "not indexed" };
        final Object locked = new Object();
        final boolean[] holding = { false };
        Thread indexing = new Thread(new Runnable() {
            public void run() {
                synchronized (GameAutomationIndex.class) {
                    synchronized (locked) {
                        holding[0] = true;
                        locked.notifyAll();

                        while (holding[0])
                            try {
                                locked.wait();
                            } catch (InterruptedException exception) {
                                return;
                            }
                    }
                }
            }
        });
        Thread jdk = new Thread(new Runnable() {
            public void run() {
                indexes[0] = GameAutomationIndex.index(null, "jdk/internal/misc/Unsafe", null) == null
                    && GameAutomationIndex.index(ClassLoader.getSystemClassLoader().getParent(), "java/sql/Date", jdkModule) == null
                    && GameAutomationIndex.index(GameAutomationIndexTest.class.getClassLoader(), "voidclient/Hidden", new ProtectionDomain(null, null)) == null
                    ? null : "indexed";
            }
        });

        indexing.start();
        synchronized (locked) {
            while (!holding[0])
                locked.wait();
        }

        jdk.start();
        jdk.join(5000L);
        boolean waited = jdk.isAlive();

        synchronized (locked) {
            holding[0] = false;
            locked.notifyAll();
        }
        indexing.join();
        jdk.join();

        Assert.assertFalse("JDK classes waited for the thread that indexes game code", waited);
        Assert.assertNull(indexes[0]);
    }

    @Test
    public void classFileUrlsStandForTheirJar() throws Exception {
        // LaunchWrapper, which Forge uses up to 1.12.2, gives every class the URL of its own jar entry.
        Assert.assertEquals(new URL("file:/game/forge.jar"), GameAutomationIndex.containingJar(new URL("jar:file:/game/forge.jar!/bib.class")));
        Assert.assertEquals(new URL("file:/game/client.jar"), GameAutomationIndex.containingJar(new URL("file:/game/client.jar")));
    }

    @Test
    public void earlierLocationsOfAUnionWin() throws Exception {
        // Forge 1.13 to 1.16 serves a class from the first of its Minecraft files that has it, such as its patched client.
        Path patched = jar(GameAutomationIndexTest.class, PatchedScreen.class);
        Path original = jar(GameAutomationIndexTest.class);

        try {
            Map<String, ClassNode> types = ModLoaderCode.readUnion(new Path[] { patched, original });

            Assert.assertTrue(types.containsKey(PatchedScreen.class.getName().replace('.', '/')));
            Assert.assertEquals(2, types.size());
        } finally {
            Files.deleteIfExists(patched);
            Files.deleteIfExists(original);
        }
    }

    @Test
    public void onlyMinecraftClassesLookForForgesMinecraft() throws Exception {
        Map<?, ?> locations = (Map<?, ?>) field(GameAutomationIndex.class, "IndexedLocations").get(null);
        Map<?, ?> failures = (Map<?, ?>) field(GameAutomationController.class, "IndexFailures").get(null);
        ClassLoader loader = GameAutomationIndexTest.class.getClassLoader();

        try {
            // Forge 1.16.5 crashed when the agent looked Forge's loader up while the JDK loaded the classes that verify signed jars.
            GameAutomationIndex.index(null, "sun/security/x509/AVA", null);
            GameAutomationIndex.index(loader, "com/example/Mod", null);
            Assert.assertFalse(locations.containsKey("forge:minecraft"));

            // Forge 1.12.2 defines the classes its patches add to Minecraft without a code source too, and reads them like the rest.
            GameAutomationIndex.index(loader, "net/minecraft/client/Patched", null);
            Assert.assertTrue(locations.containsKey("forge:minecraft"));
            Assert.assertFalse(failures.containsKey(GameAutomationController.GameCodeFailure));
        } finally {
            locations.remove("forge:minecraft");
            failures.clear();
        }
    }

    @Test
    public void launchWrapperGameCodeIsReadOnceItsRenamerIsRegistered() throws Exception {
        // FML registers its patches before its renamer, and an index of the obfuscated names would never match the running classes.
        Path game = jar(GameAutomationIndexTest.class);
        LaunchClassLoader loader = new LaunchClassLoader();
        loader.transformers.add(new PatchingTransformer());

        try {
            Assert.assertSame(ModLoaderCode.NotYet, ModLoaderCode.readLaunchWrapper(loader, game.toUri().toURL()));

            loader.transformers.add(new Renamer(GameAutomationIndexTest.class.getName(), "net.minecraft.client.Renamed"));
            Map<String, ClassNode> types = ModLoaderCode.readLaunchWrapper(loader, game.toUri().toURL());

            Assert.assertEquals(Collections.singleton(GameAutomationIndexTest.class.getName().replace('.', '/')), types.keySet());
            Assert.assertEquals(1, PatchingTransformer.calls);
        } finally {
            Files.deleteIfExists(game);
        }
    }

    private static Field field(Class<?> type, String name) throws Exception {
        Field field = type.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    private static Path jar(Class<?>... types) throws Exception {
        Path jar = Files.createTempFile("void-client-index", ".jar");
        Files.delete(jar);

        try (FileSystem fileSystem = FileSystems.newFileSystem(URI.create("jar:" + jar.toUri()), Collections.singletonMap("create", "true"))) {
            for (Class<?> type : types) {
                String name = type.getName().replace('.', '/') + ".class";
                Path entry = fileSystem.getPath(name);
                Files.createDirectories(entry.getParent());

                try (InputStream input = type.getResourceAsStream("/" + name)) {
                    Files.copy(input, entry);
                }
            }
        }

        return jar;
    }

    static final class PatchedScreen {
    }

    /** Stands in for LaunchWrapper's class loader, which the agent finds by its public transformer list. */
    public static final class LaunchClassLoader extends ClassLoader {
        final java.util.List<Object> transformers = new java.util.ArrayList<Object>();

        public java.util.List<Object> getTransformers() {
            return transformers;
        }
    }

    public static final class PatchingTransformer {
        static int calls;

        public byte[] transform(String name, String transformedName, byte[] bytes) {
            calls++;
            return bytes;
        }
    }

    public static final class Renamer {
        private final String from;
        private final String to;

        Renamer(String from, String to) {
            this.from = from;
            this.to = to;
        }

        public String remapClassName(String name) {
            return from.equals(name) ? to : name;
        }

        public byte[] transform(String name, String transformedName, byte[] bytes) {
            return bytes;
        }
    }
}
