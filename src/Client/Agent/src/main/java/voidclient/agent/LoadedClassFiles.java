package voidclient.agent;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;

/** Reads the class files of loaded classes for structural discovery. */
final class LoadedClassFiles {
    private LoadedClassFiles() {
    }

    /**
     * Reads the class file a loaded class comes from. Loaders that rename classes before defining them, such as LaunchWrapper for Forge up
     * to 1.12.2, have no class file under the running name, so the bytes the class was defined from are used instead.
     *
     * @return the class file, or {@code null} when neither can be read
     */
    static byte[] read(Instrumentation instrumentation, Class<?> type) {
        byte[] resource = readResource(type);

        return resource != null ? resource : readDefined(instrumentation, type);
    }

    static byte[] readResource(Class<?> type) {
        String resourceName = type.getName().replace('.', '/') + ".class";
        ClassLoader loader = type.getClassLoader();

        try (InputStream stream = loader == null ? ClassLoader.getSystemResourceAsStream(resourceName) : loader.getResourceAsStream(resourceName)) {
            if (stream == null)
                return null;

            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];

            for (int count = stream.read(buffer); count >= 0; count = stream.read(buffer))
                output.write(buffer, 0, count);

            return output.toByteArray();
        } catch (IOException exception) {
            return null;
        }
    }

    /** Asks the JVM for the bytes a class was defined from, which it hands to transformers when the class is retransformed. */
    static synchronized byte[] readDefined(Instrumentation instrumentation, final Class<?> type) {
        if (instrumentation == null || !instrumentation.isRetransformClassesSupported() || !instrumentation.isModifiableClass(type))
            return null;

        final byte[][] captured = new byte[1][];
        ClassFileTransformer capture = new ClassFileTransformer() {
            @Override
            public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined, ProtectionDomain domain, byte[] classFile) {
                if (classBeingRedefined == type)
                    captured[0] = classFile.clone();

                return null;
            }
        };

        instrumentation.addTransformer(capture, true);

        try {
            instrumentation.retransformClasses(type);
        } catch (Throwable exception) {
            return null;
        } finally {
            instrumentation.removeTransformer(capture);
        }

        return captured[0];
    }
}
