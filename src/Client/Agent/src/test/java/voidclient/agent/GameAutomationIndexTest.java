package voidclient.agent;

import java.io.InputStream;
import java.net.URI;
import java.nio.file.FileSystem;
import java.nio.file.FileSystems;
import java.nio.file.Files;
import java.nio.file.Path;
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
}
