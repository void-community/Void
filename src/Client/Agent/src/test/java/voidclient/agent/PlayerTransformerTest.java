package voidclient.agent;

import com.mojang.authlib.GameProfile;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import org.junit.Test;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

public class PlayerTransformerTest {
    private static final List<Object> Registered = new ArrayList<Object>();

    @Test
    public void profileFieldRegistersTheInstanceFromALoaderThatCannotSeeTheAgent() throws Exception {
        Registered.clear();

        Object instance = constructWithBootstrapHooksOnly("record");

        assertEquals(Collections.singletonList(instance), Registered);
    }

    @Test
    public void failingRegistrationDoesNotBreakTheConstructor() throws Exception {
        assertNotNull(constructWithBootstrapHooksOnly("fail"));
    }

    @Test
    public void staticProfileDoesNotRegisterAnInstance() throws Exception {
        assertNull(PlayerTransformer.instrumentPlayerConstructors(bytes(StaticProfile.class)));
    }

    @Test
    public void profileParameterWithoutAFieldDoesNotRegisterAnInstance() throws Exception {
        assertNull(PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileParameter.class)));
    }

    public static void record(Object value) {
        Registered.add(value);
    }

    public static void fail(Object value) {
        throw new IllegalStateException("tracker unavailable");
    }

    /**
     * Mirrors NeoForge 1.21.1, where mod classes are defined by a loader that can't see the agent jar but reaches the
     * bootstrap class path, which holds only JDK classes and the game hooks jar.
     */
    private static Object constructWithBootstrapHooksOnly(String registration) throws Exception {
        byte[] transformed = PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileHolder.class));
        assertNotNull(transformed);
        MethodHandle target = MethodHandles.lookup().findStatic(PlayerTransformerTest.class, registration, MethodType.methodType(void.class, Object.class));
        Path hooksJar = VoidClientAgent.writeGameHooksJar();

        try (URLClassLoader bootstrap = new URLClassLoader(new URL[] { hooksJar.toUri().toURL() }, null)) {
            bootstrap.loadClass(VoidClientAgent.GameHooksName.replace('/', '.'))
                .getMethod("install", MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class)
                .invoke(null, target, null, null, null, null);

            return new BytecodeLoader(bootstrap).define(transformed).getConstructor().newInstance();
        } finally {
            Files.delete(hooksJar);
        }
    }

    private static byte[] bytes(Class<?> type) throws Exception {
        try (InputStream input = type.getResourceAsStream("/" + type.getName().replace('.', '/') + ".class")) {
            assertNotNull(input);
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int length;
            while ((length = input.read(buffer)) != -1)
                output.write(buffer, 0, length);
            return output.toByteArray();
        }
    }

    public static class ProfileHolder {
        public GameProfile profile;
    }

    public static class StaticProfile {
        public static GameProfile profile;
    }

    public static class ProfileParameter {
        public ProfileParameter(GameProfile profile) { }
    }

    private static class BytecodeLoader extends ClassLoader {
        BytecodeLoader(ClassLoader parent) {
            super(parent);
        }

        Class<?> define(byte[] bytes) {
            return defineClass(null, bytes, 0, bytes.length);
        }
    }
}
