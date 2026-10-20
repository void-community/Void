package voidclient.agent;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.jar.JarEntry;
import java.util.jar.JarOutputStream;

/** Builds a jar with only GameHooks, the way the build packages it for the bootstrap class path. */
final class GameHooksJar {
    private GameHooksJar() {
    }

    static Path write() throws IOException {
        String entryName = VoidClientAgent.GameHooksName + ".class";
        Path jarPath = Files.createTempFile("void-client-game-hooks", ".jar");
        jarPath.toFile().deleteOnExit();

        try (InputStream input = VoidClientAgent.class.getResourceAsStream("/" + entryName);
             JarOutputStream output = new JarOutputStream(Files.newOutputStream(jarPath))) {
            if (input == null)
                throw new IOException("The test classes do not contain " + entryName);

            output.putNextEntry(new JarEntry(entryName));
            byte[] buffer = new byte[8192];
            int length;

            while ((length = input.read(buffer)) != -1)
                output.write(buffer, 0, length);

            output.closeEntry();
        }

        return jarPath;
    }
}
