package voidclient.agent;

import java.io.File;
import java.lang.invoke.MethodHandle;
import java.lang.reflect.Field;
import java.lang.reflect.Modifier;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Base64;
import java.util.Enumeration;
import java.util.concurrent.TimeUnit;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import org.junit.Test;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

/**
 * Checks the packaged agent jar the way game class loaders see it. Issue #1621: Fabric refused GameHooks because the agent jar also
 * held its class file, which Fabric found first and doesn't trust.
 */
public class PackagedAgentIT {
    private static final File AgentJar = new File(System.getProperty("agentJar", "target/void-client-agent.jar"));

    @Test
    public void onlyTheBootClassPathJarHoldsGameHooks() throws Exception {
        try (JarFile agent = new JarFile(AgentJar)) {
            for (Enumeration<JarEntry> entries = agent.entries(); entries.hasMoreElements(); ) {
                String name = entries.nextElement().getName();
                assertFalse(name + " must only be in the hooks jar", name.startsWith("voidclient/bootstrap/"));
            }

            String bootClassPath = agent.getManifest().getMainAttributes().getValue("Boot-Class-Path");
            assertNotNull("The agent manifest has no Boot-Class-Path", bootClassPath);

            try (JarFile hooks = new JarFile(new File(AgentJar.getParentFile(), bootClassPath))) {
                assertNotNull(hooks.getEntry(VoidClientAgent.GameHooksName + ".class"));
            }
        }
    }

    @Test
    public void fabricLoadsTheInstalledGameHooks() throws Exception {
        Path directory = Files.createTempDirectory("void-client-packaged-agent");
        Path output = directory.resolve("output.log");
        String arguments = "descriptor=" + encode(directory.resolve("agent.port").toString()) + ";token=" + encode("token");
        String testClasses = new File(KnotDelegation.class.getProtectionDomain().getCodeSource().getLocation().toURI()).getPath();
        Process process = new ProcessBuilder(
            Paths.get(System.getProperty("java.home"), "bin", "java").toString(),
            "-javaagent:" + AgentJar.getAbsolutePath() + "=" + arguments,
            "-cp",
            testClasses,
            KnotDelegation.class.getName()
        ).redirectErrorStream(true).redirectOutput(output.toFile()).start();

        try {
            boolean exited = process.waitFor(60, TimeUnit.SECONDS);
            String log = new String(Files.readAllBytes(output), StandardCharsets.UTF_8);

            assertTrue("The game JVM didn't exit: " + log, exited);
            assertEquals(log, 0, process.exitValue());
        } finally {
            process.destroyForcibly();

            for (File file : directory.toFile().listFiles())
                Files.deleteIfExists(file.toPath());

            Files.deleteIfExists(directory);
        }
    }

    private static String encode(String value) {
        return Base64.getUrlEncoder().encodeToString(value.getBytes(StandardCharsets.UTF_8));
    }

    /** Loads GameHooks the way Fabric's class loader loads a class it doesn't own, in a JVM started with the packaged agent. */
    public static final class KnotDelegation {
        private KnotDelegation() {
        }

        public static void main(String[] arguments) throws Exception {
            String name = VoidClientAgent.GameHooksName.replace('/', '.');
            String fileName = VoidClientAgent.GameHooksName + ".class";
            ClassLoader system = ClassLoader.getSystemClassLoader();
            ClassLoader platform = platformClassLoader();
            URL url = system.getResource(fileName);

            if (url != null && platform.getResource(fileName) == null)
                throw new ClassNotFoundException("can't load class " + name + " at " + url + " as it hasn't been exposed to the game");

            Class<?> hooks = url == null ? platform.loadClass(name) : system.loadClass(name);

            for (Field field : hooks.getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) || field.getType() != MethodHandle.class)
                    continue;

                field.setAccessible(true);

                if (field.get(null) == null)
                    throw new IllegalStateException("The agent didn't install " + field.getName() + " in the GameHooks the game loads");
            }
        }

        // Fabric uses the platform class loader where Java has one, and a loader with only the bootstrap loader above it before.
        private static ClassLoader platformClassLoader() {
            try {
                return (ClassLoader) ClassLoader.class.getMethod("getPlatformClassLoader").invoke(null);
            } catch (ReflectiveOperationException exception) {
                return new ClassLoader(null) {
                };
            }
        }
    }
}
