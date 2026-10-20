package voidclient.agent;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.lang.reflect.Method;
import java.net.URL;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import org.objectweb.asm.tree.ClassNode;

/**
 * Reads game code the way mod loaders that change it before the JVM sees it serve it. Everything is found through the loaders' public
 * APIs and by structure, so other versions of them work as well.
 */
final class ModLoaderCode {
    /** Returned while a loader isn't ready to serve its game code yet, so nothing is remembered about it. */
    static final Map<String, ClassNode> NotYet = Collections.unmodifiableMap(new HashMap<String, ClassNode>());
    private static final String ForgeLoaderName = "net.minecraftforge.fml.loading.FMLLoader";
    private static final Map<Class<?>, Method> TransformMethods = new HashMap<Class<?>, Method>();

    private ModLoaderCode() {
    }

    /** Reads code from a location, such as a jar. */
    interface TypeReader {
        Map<String, ClassNode> read() throws Exception;
    }

    /**
     * Reads Minecraft's classes for Forge 1.13 to 1.16, which defines them without a code source. Its loader lists the files they come
     * from, and the first file with a class is the one Forge serves it from.
     *
     * @return the classes, none outside such a Forge, where other loaders define classes without a code source too, {@code null} when
     *     none of the files can be read, or {@link #NotYet} before Forge knows the files
     */
    static Map<String, ClassNode> readForgeMinecraft() throws Exception {
        Method minecraftPaths;

        try {
            minecraftPaths = Class.forName(ForgeLoaderName, false, ClassLoader.getSystemClassLoader()).getMethod("getMCPaths");
        } catch (ClassNotFoundException | NoSuchMethodException | LinkageError exception) {
            return Collections.emptyMap();
        }

        Object paths = minecraftPaths.invoke(null);

        return paths instanceof Path[] ? readUnion((Path[]) paths) : NotYet;
    }

    /**
     * Reads several locations as one, where a class in an earlier location replaces the same class in a later one.
     *
     * @return the classes, or {@code null} when none of the locations can be read
     */
    static Map<String, ClassNode> readUnion(Path[] paths) throws Exception {
        Map<String, ClassNode> types = null;

        for (int index = paths.length - 1; index >= 0; index--) {
            Map<String, ClassNode> pathTypes = GameAutomationIndex.readTypes(paths[index].toUri());

            if (pathTypes == null)
                continue;

            if (types == null)
                types = new HashMap<String, ClassNode>();

            types.putAll(pathTypes);
        }

        return types;
    }

    /** Tells whether classes come from LaunchWrapper, which Forge uses up to 1.12.2, by its public transformer list. */
    static boolean isLaunchWrapper(ClassLoader loader) {
        return loader != null && transformersMethod(loader) != null;
    }

    /**
     * Reads a jar the way LaunchWrapper serves its classes. FML patches Minecraft's classes and renames them from obfuscated names before
     * the game runs them, so the jar on disk has other names and code. Only those two transformers run here, never coremods or Mixin,
     * whose effects can't be repeated.
     *
     * @return the classes, or {@link #NotYet} while FML has registered its patches but not its renamer, which comes later
     */
    static Map<String, ClassNode> readLaunchWrapper(ClassLoader loader, URL location) throws Exception {
        List<?> transformers = (List<?>) transformersMethod(loader).invoke(loader);
        List<Object> applied = new ArrayList<Object>();
        Method rename = null;
        Object renamer = null;
        boolean patched = false;

        for (Object transformer : transformers) {
            Method transformerRename = renameMethod(transformer);

            if (renamer == null && transformerRename != null) {
                renamer = transformer;
                rename = transformerRename;
                applied.add(transformer);
            } else if ("PatchingTransformer".equals(transformer.getClass().getSimpleName())) {
                patched = true;
                applied.add(transformer);
            }
        }

        if (renamer == null)
            return patched ? NotYet : GameAutomationIndex.readTypes(location.toURI());

        File file = "file".equals(location.getProtocol()) ? new File(location.toURI()) : null;

        if (file == null || !file.isFile())
            return GameAutomationIndex.readTypes(location.toURI());

        JarFile jar = new JarFile(file);

        try {
            List<JarEntry> entries = new ArrayList<JarEntry>();
            boolean renamed = false;

            for (java.util.Enumeration<JarEntry> all = jar.entries(); all.hasMoreElements(); ) {
                JarEntry entry = all.nextElement();

                if (!entry.getName().endsWith(".class"))
                    continue;

                entries.add(entry);
                String name = className(entry);
                renamed |= !renamed && !name.equals(rename.invoke(renamer, name));
            }

            // Only Minecraft's own jar has obfuscated names, and the jars of mods and libraries are served as they are.
            if (!renamed)
                return GameAutomationIndex.readTypes(location.toURI());

            Map<String, ClassNode> types = new HashMap<String, ClassNode>();

            for (JarEntry entry : entries) {
                String name = className(entry);
                byte[] bytes = transform(applied, name, (String) rename.invoke(renamer, name), readBytes(jar, entry));

                if (bytes != null)
                    GameAutomationIndex.addType(types, new ByteArrayInputStream(bytes));
            }

            return types;
        } finally {
            jar.close();
        }
    }

    private static byte[] transform(List<Object> transformers, String name, String transformedName, byte[] bytes) {
        byte[] current = bytes;

        try {
            for (Object transformer : transformers) {
                if (current == null)
                    return null;

                current = (byte[]) transformMethod(transformer).invoke(transformer, name, transformedName, current);
            }
        } catch (Throwable exception) {
            // A class the transformers can't handle is left out instead of the whole jar.
            return null;
        }

        return current;
    }

    private static String className(JarEntry entry) {
        return entry.getName().substring(0, entry.getName().length() - ".class".length()).replace('/', '.');
    }

    private static byte[] readBytes(JarFile jar, JarEntry entry) throws java.io.IOException {
        InputStream input = jar.getInputStream(entry);

        try {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];

            for (int read = input.read(buffer); read > 0; read = input.read(buffer))
                output.write(buffer, 0, read);

            return output.toByteArray();
        } finally {
            input.close();
        }
    }

    private static Method transformersMethod(ClassLoader loader) {
        try {
            Method method = loader.getClass().getMethod("getTransformers");

            return List.class.isAssignableFrom(method.getReturnType()) ? method : null;
        } catch (NoSuchMethodException exception) {
            return null;
        }
    }

    /** The renamer is the transformer LaunchWrapper asks for class names, which has a public remapClassName. */
    private static Method renameMethod(Object transformer) {
        try {
            return accessible(transformer.getClass().getMethod("remapClassName", String.class));
        } catch (NoSuchMethodException exception) {
            return null;
        }
    }

    private static synchronized Method transformMethod(Object transformer) throws NoSuchMethodException {
        Method method = TransformMethods.get(transformer.getClass());

        if (method == null) {
            method = accessible(transformer.getClass().getMethod("transform", String.class, String.class, byte[].class));
            TransformMethods.put(transformer.getClass(), method);
        }

        return method;
    }

    private static Method accessible(Method method) {
        try {
            method.setAccessible(true);
        } catch (RuntimeException exception) {
            // A public method of a public class doesn't need it.
        }

        return method;
    }
}
