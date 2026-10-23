package voidclient.agent;

import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.LabelNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.VarInsnNode;

public final class ButtonTransitionTest {
    @Test
    public void buttonComparedWithZeroIsControlZero() {
        // Minecraft 1.12.2's disconnected screen: if (button.id == 0) mc.displayGuiScreen(parentScreen);
        ClassNode screen = new ClassNode();
        screen.name = "bky";
        screen.superName = "blk";
        MethodNode action = new MethodNode(Opcodes.ACC_PROTECTED, "a", "(Lbja;)V", null, null);
        LabelNode skip = new LabelNode();
        action.instructions.add(new VarInsnNode(Opcodes.ALOAD, 1));
        action.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "bja", "k", "I"));
        action.instructions.add(new JumpInsnNode(Opcodes.IFNE, skip));
        action.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        action.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "bky", "j", "Lbib;"));
        action.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        action.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "bky", "h", "Lblk;"));
        action.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "bib", "a", "(Lblk;)V", false));
        action.instructions.add(skip);
        action.instructions.add(new InsnNode(Opcodes.RETURN));
        screen.methods.add(action);
        Map<String, String> hierarchy = new HashMap<String, String>();
        hierarchy.put(screen.name, screen.superName);

        TransitionPlan plan = GameAutomationIndex.discoverTransitions(Collections.singletonMap(screen.name, screen), hierarchy, "blk",
            new GameAutomationIndex.ScreenSetter("bib", "a", "(Lblk;)V")).get(screen.name).get(0);

        Assert.assertEquals(Integer.valueOf(0), plan.controlId);
        Assert.assertEquals("k", plan.controlIdFieldName);
        Assert.assertTrue(plan.targetsParent);
    }

    @Test
    public void castScreenArgumentsKeepTheirTarget() {
        // Forge recompiles the classes it patches, so (Screen) null and (Screen) this come with a checkcast before the setter call.
        TransitionPlan closes = castTransition(new InsnNode(Opcodes.ACONST_NULL));
        TransitionPlan stays = castTransition(new VarInsnNode(Opcodes.ALOAD, 0));

        Assert.assertFalse(closes.targetsParent || closes.targetsSelf);
        Assert.assertTrue(closes.targetScreenNames.isEmpty());
        Assert.assertTrue(stays.targetsSelf);
    }

    @Test
    public void screenTakenAsParameterIsNoTransition() {
        // Issue #1616: Minecraft 1.21.1's accessibility onboarding screen sets the screen its caller passes, which the agent can only fill
        // with the current screen, so the transition changed nothing.
        ClassNode screen = new ClassNode();
        screen.name = "AccessibilityOnboardingScreen";
        screen.superName = "Screen";
        MethodNode forward = new MethodNode(Opcodes.ACC_PRIVATE, "lambda$closeAndSetScreen$4", "(LScreen;)V", null, null);
        forward.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        forward.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "AccessibilityOnboardingScreen", "minecraft", "LMinecraft;"));
        forward.instructions.add(new VarInsnNode(Opcodes.ALOAD, 1));
        forward.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "Minecraft", "setScreen", "(LScreen;)V", false));
        forward.instructions.add(new InsnNode(Opcodes.RETURN));
        MethodNode back = new MethodNode(Opcodes.ACC_PUBLIC, "onClose", "()V", null, null);
        back.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        back.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "AccessibilityOnboardingScreen", "minecraft", "LMinecraft;"));
        back.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        back.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "AccessibilityOnboardingScreen", "lastScreen", "LScreen;"));
        back.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "Minecraft", "setScreen", "(LScreen;)V", false));
        back.instructions.add(new InsnNode(Opcodes.RETURN));
        screen.methods.add(forward);
        screen.methods.add(back);

        List<TransitionPlan> transitions = GameAutomationIndex.discoverTransitions(Collections.singletonMap(screen.name, screen),
            Collections.singletonMap(screen.name, screen.superName), "Screen", new GameAutomationIndex.ScreenSetter("Minecraft", "setScreen", "(LScreen;)V"))
            .get(screen.name);

        // A parent the screen keeps in a field is still a way back.
        Assert.assertEquals(1, transitions.size());
        Assert.assertEquals("onClose", transitions.get(0).methodName);
        Assert.assertTrue(transitions.get(0).targetsParent);
    }

    private static TransitionPlan castTransition(org.objectweb.asm.tree.AbstractInsnNode argument) {
        ClassNode screen = new ClassNode();
        screen.name = "ChatScreen";
        screen.superName = "Screen";
        MethodNode close = new MethodNode(Opcodes.ACC_PUBLIC, "keyPressed", "()V", null, null);
        close.instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        close.instructions.add(new FieldInsnNode(Opcodes.GETFIELD, "ChatScreen", "minecraft", "LMinecraft;"));
        close.instructions.add(argument);
        close.instructions.add(new org.objectweb.asm.tree.TypeInsnNode(Opcodes.CHECKCAST, "Screen"));
        close.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "Minecraft", "setScreen", "(LScreen;)V", false));
        close.instructions.add(new InsnNode(Opcodes.RETURN));
        screen.methods.add(close);

        return GameAutomationIndex.discoverTransitions(Collections.singletonMap(screen.name, screen), Collections.singletonMap(screen.name, screen.superName),
            "Screen", new GameAutomationIndex.ScreenSetter("Minecraft", "setScreen", "(LScreen;)V")).get(screen.name).get(0);
    }
}
