package voidclient.agent;

import com.mojang.authlib.GameProfile;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.lang.reflect.Field;
import java.util.List;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.FieldNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.VarInsnNode;

public final class PositionSourceDiscoveryTest {
    @Test
    public void discoversLegacyCoordinateFieldsWithoutNames() {
        ClassNode type = createDistanceType(false);

        PositionSource[] sources = PositionSourceDiscovery.findPositionSources(type);

        Assert.assertNotNull(sources);
        Assert.assertEquals("a", sources[0].name);
        Assert.assertEquals("b", sources[1].name);
        Assert.assertEquals("c", sources[2].name);
        Assert.assertFalse(sources[0].method);
    }

    @Test
    public void discoversModernCoordinateGettersWithoutNames() {
        ClassNode type = createDistanceType(true);

        PositionSource[] sources = PositionSourceDiscovery.findPositionSources(type);

        Assert.assertNotNull(sources);
        Assert.assertEquals("a", sources[0].name);
        Assert.assertEquals("b", sources[1].name);
        Assert.assertEquals("c", sources[2].name);
        Assert.assertTrue(sources[0].method);
    }

    @Test
    public void discoversCurrentBodyAndHeadYawWithoutNames() {
        ClassNode type = createLivingRotationType();

        RotationSources sources = RotationSourceDiscovery.findRotationSources(type);

        Assert.assertNotNull(sources);
        Assert.assertEquals("body", sources.bodyYaw.name);
        Assert.assertEquals("head", sources.headYaw.name);
        Assert.assertNull(sources.headPitch);
    }

    @Test
    public void discoversHeadPitchFromRotationSetterWithoutNames() {
        ClassNode type = createEntityRotationType(false);

        RotationSources sources = RotationSourceDiscovery.findRotationSources(type);

        Assert.assertNotNull(sources);
        Assert.assertNull(sources.bodyYaw);
        Assert.assertNull(sources.headYaw);
        Assert.assertEquals("pitch", sources.headPitch.name);
    }

    @Test
    public void discoversHeadPitchThroughUnnamedSetterMethods() {
        ClassNode type = createEntityRotationType(true);

        RotationSources sources = RotationSourceDiscovery.findRotationSources(type);

        Assert.assertNotNull(sources);
        Assert.assertEquals("pitch", sources.headPitch.name);
    }

    @Test
    public void normalizesPlayerHeightFromAnUnnamedBoundingBox() {
        Double minimumY = Tracker.boundingBoxMinimumY(new SyntheticEntity(), 20.5, -7.5);

        Assert.assertNotNull(minimumY);
        Assert.assertEquals(100.0, minimumY.doubleValue(), 0.0);
    }

    @Test
    public void identifiesDuplicatePlayersByProfileIdentity() {
        Assert.assertTrue(Tracker.samePlayerIdentity("first-id", "old-name", "first-id", "new-name"));
        Assert.assertTrue(Tracker.samePlayerIdentity(null, "same-name", "second-id", "same-name"));
        Assert.assertFalse(Tracker.samePlayerIdentity("first-id", "same-name", "second-id", "same-name"));
        Assert.assertFalse(Tracker.samePlayerIdentity(null, null, null, null));
    }

    @Test
    public void identifiesPlayerCollectionsFromGenericElementStructure() throws ReflectiveOperationException {
        Field players = SyntheticWorld.class.getDeclaredField("players");
        Field entities = SyntheticWorld.class.getDeclaredField("entities");

        Assert.assertTrue(Tracker.hasPlayerElementType(players.getGenericType(), 0));
        Assert.assertFalse(Tracker.hasPlayerElementType(entities.getGenericType(), 0));
    }

    @Test
    public void instrumentsStructurallyIdentifiedPlayerConstructors() throws IOException {
        byte[] instrumented = PlayerTransformer.instrumentPlayerConstructors(classBytes(SyntheticPlayer.class));

        Assert.assertNotNull(instrumented);
        ClassNode type = new ClassNode();
        new ClassReader(instrumented).accept(type, 0);
        boolean registrationFound = false;

        for (MethodNode method : type.methods) {
            for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                if (instruction instanceof MethodInsnNode) {
                    MethodInsnNode invocation = (MethodInsnNode) instruction;
                    registrationFound |= VoidClientAgent.GameHooksName.equals(invocation.owner) && "registerPlayer".equals(invocation.name);
                }
            }
        }

        Assert.assertTrue(registrationFound);
        Assert.assertNull(PlayerTransformer.instrumentPlayerConstructors(classBytes(SyntheticEntity.class)));
    }

    private static byte[] classBytes(Class<?> type) throws IOException {
        String resourceName = "/" + type.getName().replace('.', '/') + ".class";

        try (InputStream stream = type.getResourceAsStream(resourceName)) {
            Assert.assertNotNull(stream);
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[4096];
            int count;

            while ((count = stream.read(buffer)) >= 0)
                output.write(buffer, 0, count);

            return output.toByteArray();
        }
    }

    private static ClassNode createDistanceType(boolean methods) {
        ClassNode type = new ClassNode();
        type.name = "x/y";
        MethodNode distance = new MethodNode(Opcodes.ACC_PUBLIC, "z", "(DDD)D", null, null);
        appendCoordinate(distance, type.name, "a", 1, methods);
        appendCoordinate(distance, type.name, "b", 3, methods);
        distance.instructions.add(new InsnNode(Opcodes.DADD));
        appendCoordinate(distance, type.name, "c", 5, methods);
        distance.instructions.add(new InsnNode(Opcodes.DADD));
        distance.instructions.add(new InsnNode(Opcodes.DRETURN));
        type.methods.add(distance);
        return type;
    }

    private static ClassNode createLivingRotationType() {
        ClassNode type = new ClassNode();
        type.name = "x/living";
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "body", "F", null, null));
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "previousBody", "F", null, null));
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "head", "F", null, null));
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "previousHead", "F", null, null));
        MethodNode turnHead = new MethodNode(Opcodes.ACC_PROTECTED, "a", "(FF)F", null, null);
        turnHead.instructions.add(new VarInsnNode(Opcodes.FLOAD, 1));
        turnHead.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        turnHead.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, type.name, "body", "F"));
        turnHead.instructions.add(new InsnNode(Opcodes.FSUB));
        turnHead.instructions.add(new InsnNode(Opcodes.POP));
        turnHead.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        turnHead.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        turnHead.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, type.name, "body", "F"));
        turnHead.instructions.add(new VarInsnNode(Opcodes.FLOAD, 1));
        turnHead.instructions.add(new InsnNode(Opcodes.FADD));
        turnHead.instructions.add(new FieldInsnNode(Opcodes.PUTFIELD, type.name, "body", "F"));
        turnHead.instructions.add(new VarInsnNode(Opcodes.FLOAD, 2));
        turnHead.instructions.add(new InsnNode(Opcodes.FRETURN));
        type.methods.add(turnHead);
        MethodNode snapshot = new MethodNode(Opcodes.ACC_PUBLIC, "b", "()V", null, null);
        appendFieldCopy(snapshot, type.name, "head", "previousHead");
        appendFieldCopy(snapshot, type.name, "body", "previousBody");
        snapshot.instructions.add(new InsnNode(Opcodes.RETURN));
        type.methods.add(snapshot);
        return type;
    }

    private static void appendFieldCopy(MethodNode method, String owner, String source, String target) {
        method.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        method.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        method.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, owner, source, "F"));
        method.instructions.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, target, "F"));
    }

    private static ClassNode createEntityRotationType(boolean methods) {
        ClassNode type = new ClassNode();
        type.name = "x/entity";
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "yaw", "F", null, null));
        type.fields.add(new FieldNode(Opcodes.ACC_PRIVATE, "pitch", "F", null, null));

        if (methods) {
            type.methods.add(createRotationSetter(type.name, "c", "yaw"));
            type.methods.add(createRotationSetter(type.name, "d", "pitch"));
        }

        MethodNode setRotation = new MethodNode(Opcodes.ACC_PUBLIC, "a", "(FF)V", null, null);
        appendRotationAssignment(setRotation, type.name, methods ? "c" : "yaw", 1, methods);
        appendRotationAssignment(setRotation, type.name, methods ? "d" : "pitch", 2, methods);
        setRotation.instructions.add(new InsnNode(Opcodes.RETURN));
        type.methods.add(setRotation);
        return type;
    }

    private static MethodNode createRotationSetter(String owner, String name, String field) {
        MethodNode setter = new MethodNode(Opcodes.ACC_PRIVATE, name, "(F)V", null, null);
        setter.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        setter.instructions.add(new VarInsnNode(Opcodes.FLOAD, 1));
        setter.instructions.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, field, "F"));
        setter.instructions.add(new InsnNode(Opcodes.RETURN));
        return setter;
    }

    private static void appendRotationAssignment(MethodNode method, String owner, String name, int variable, boolean setter) {
        method.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        method.instructions.add(new VarInsnNode(Opcodes.FLOAD, variable));
        method.instructions.add(new LdcInsnNode(Float.valueOf(360.0F)));
        method.instructions.add(new InsnNode(Opcodes.FREM));

        if (setter)
            method.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, owner, name, "(F)V", false));
        else
            method.instructions.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, name, "F"));
    }

    private static void appendCoordinate(MethodNode distance, String owner, String name, int variable, boolean methods) {
        distance.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));

        if (methods)
            distance.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, owner, name, "()D", false));
        else
            distance.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, owner, name, "D"));

        distance.instructions.add(new VarInsnNode(Opcodes.DLOAD, variable));
        distance.instructions.add(new InsnNode(Opcodes.DSUB));
        distance.instructions.add(new InsnNode(Opcodes.DUP2));
        distance.instructions.add(new InsnNode(Opcodes.DMUL));
    }

    private static final class SyntheticEntity {
        private final SyntheticBox value = new SyntheticBox();
    }

    private static final class SyntheticPlayer {
        private final GameProfile profile = new GameProfile();
    }

    private static final class SyntheticWorld {
        private List<SyntheticPlayer> players;
        private List<SyntheticEntity> entities;
    }

    private static final class SyntheticBox {
        private final double first = 20.2;
        private final double second = 100.0;
        private final double third = -7.8;
        private final double fourth = 20.8;
        private final double fifth = 101.8;
        private final double sixth = -7.2;
    }
}
