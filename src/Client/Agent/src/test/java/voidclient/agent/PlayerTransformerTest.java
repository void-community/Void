package voidclient.agent;

import com.mojang.authlib.GameProfile;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.function.Consumer;
import org.junit.Test;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

public class PlayerTransformerTest {
    @Test
    public void profileFieldRegistersTheInstanceFromALoaderThatCannotSeeTheAgent() throws Exception {
        final List<Object> registered = new ArrayList<Object>();

        Object instance = constructWithBootstrapHookOnly(new Consumer<Object>() {
            @Override
            public void accept(Object value) {
                registered.add(value);
            }
        });

        assertEquals(Collections.singletonList(instance), registered);
    }

    @Test
    public void failingRegistrationDoesNotBreakTheConstructor() throws Exception {
        assertNotNull(constructWithBootstrapHookOnly(new Consumer<Object>() {
            @Override
            public void accept(Object value) {
                throw new IllegalStateException("tracker unavailable");
            }
        }));
    }

    @Test
    public void staticProfileDoesNotRegisterAnInstance() throws Exception {
        assertNull(PlayerTransformer.instrumentPlayerConstructors(bytes(StaticProfile.class)));
    }

    @Test
    public void profileParameterWithoutAFieldDoesNotRegisterAnInstance() throws Exception {
        assertNull(PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileParameter.class)));
    }

    /**
     * Mirrors NeoForge 1.21.1, where mod classes are defined by a loader that can't see the agent jar but reaches the
     * bootstrap class path, which holds only JDK classes and the player hook jar.
     */
    private static Object constructWithBootstrapHookOnly(Consumer<Object> registration) throws Exception {
        byte[] transformed = PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileHolder.class));
        assertNotNull(transformed);
        Path hookJar = VoidClientAgent.writePlayerHookJar();

        try (URLClassLoader bootstrap = new URLClassLoader(new URL[] { hookJar.toUri().toURL() }, null)) {
            bootstrap.loadClass(PlayerTransformer.HookName.replace('/', '.')).getMethod("install", Consumer.class).invoke(null, registration);

            return new BytecodeLoader(bootstrap).define(transformed).getConstructor().newInstance();
        } finally {
            Files.delete(hookJar);
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
