package voidclient.agent;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import org.junit.Assert;
import org.junit.Test;

public final class LoadedClassFilesTest {
    @Test
    public void classesAreReadFromTheirClassFile() throws Exception {
        Assert.assertArrayEquals(classFile(Defined.class), LoadedClassFiles.read(null, Defined.class));
    }

    @Test
    public void renamedClassesAreReadFromTheBytesTheyWereDefinedFrom() throws Exception {
        // LaunchWrapper, which Forge uses up to 1.12.2, defines Minecraft classes under names no class file has.
        byte[] definition = classFile(Defined.class);
        Class<?> renamed = new RenamingLoader().define(definition);
        List<ClassFileTransformer> transformers = new ArrayList<ClassFileTransformer>();

        Assert.assertArrayEquals(definition, LoadedClassFiles.read(instrumentation(renamed, definition, transformers), renamed));
        Assert.assertTrue(transformers.isEmpty());
    }

    private static Instrumentation instrumentation(final Class<?> type, final byte[] definition, final List<ClassFileTransformer> transformers) {
        InvocationHandler handler = new InvocationHandler() {
            @Override
            public Object invoke(Object proxy, Method method, Object[] arguments) throws Throwable {
                String name = method.getName();

                if (name.equals("isRetransformClassesSupported") || name.equals("isModifiableClass"))
                    return true;

                if (name.equals("addTransformer")) {
                    transformers.add((ClassFileTransformer) arguments[0]);
                    return null;
                }

                if (name.equals("removeTransformer"))
                    return transformers.remove(arguments[0]);

                if (!name.equals("retransformClasses"))
                    throw new UnsupportedOperationException(name);

                for (ClassFileTransformer transformer : new ArrayList<ClassFileTransformer>(transformers))
                    transformer.transform(type.getClassLoader(), type.getName().replace('.', '/'), type, null, definition.clone());

                return null;
            }
        };

        return (Instrumentation) Proxy.newProxyInstance(LoadedClassFilesTest.class.getClassLoader(),new Class<?>[] { Instrumentation.class }, handler);
    }

    private static byte[] classFile(Class<?> type) throws Exception {
        try (InputStream input = type.getResourceAsStream("/" + type.getName().replace('.', '/') + ".class")) {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];

            for (int count = input.read(buffer); count >= 0; count = input.read(buffer))
                output.write(buffer, 0, count);

            return output.toByteArray();
        }
    }

    public static final class Defined {
    }

    /** Stands in for a class loader that renames classes, so none of them has a class file under its running name. */
    static final class RenamingLoader extends ClassLoader {
        Class<?> define(byte[] classFile) {
            return defineClass(null, classFile, 0, classFile.length);
        }

        @Override
        public URL getResource(String name) {
            return null;
        }
    }
}
