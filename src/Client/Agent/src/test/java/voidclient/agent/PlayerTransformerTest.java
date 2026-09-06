package voidclient.agent;

import com.mojang.authlib.GameProfile;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import org.junit.Test;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertSame;

public class PlayerTransformerTest {
    @Test
    public void profileFieldStillRegistersTheConstructedInstance() throws Exception {
        byte[] transformed = PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileHolder.class));
        assertNotNull(transformed);
        ClassNode type = new ClassNode();
        new ClassReader(transformed).accept(type, 0);
        for (MethodNode method : type.methods)
            for (AbstractInsnNode instruction : method.instructions)
                if (instruction instanceof MethodInsnNode) {
                    MethodInsnNode call = (MethodInsnNode) instruction;
                    if ("voidclient/agent/Tracker".equals(call.owner) && "registerPlayer".equals(call.name))
                        call.owner = Probe.class.getName().replace('.', '/');
                }
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        type.accept(writer);
        Probe.registered = null;
        Object instance = new BytecodeLoader().define(writer.toByteArray()).getConstructor().newInstance();
        assertSame(instance, Probe.registered);
    }

    @Test
    public void profileParameterWithoutAFieldDoesNotRegisterAnInstance() throws Exception {
        assertNull(PlayerTransformer.instrumentPlayerConstructors(bytes(ProfileParameter.class)));
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

    public static class ProfileParameter {
        public ProfileParameter(GameProfile profile) { }
    }

    public static class Probe {
        static Object registered;

        public static void registerPlayer(Object instance) {
            registered = instance;
        }
    }

    private static class BytecodeLoader extends ClassLoader {
        Class<?> define(byte[] bytes) {
            return defineClass(null, bytes, 0, bytes.length);
        }
    }
}
