import math

from ncma_gameplay import Behaviour, export


class RotatorBehaviour(Behaviour):
    degrees_per_second = export(90.0, display_name="Degrees Per Second", category="Rotation")
    clockwise = export(False, category="Rotation")
    multiplier = export(1, category="Rotation")

    def on_update(self, delta_seconds: float) -> None:
        transform = self.node.local_transform
        angle = math.radians(self.degrees_per_second * self.multiplier * delta_seconds)
        if self.clockwise:
            angle = -angle
        sine, cosine = math.sin(angle * 0.5), math.cos(angle * 0.5)
        x, y, z, w = transform.rotation_x, transform.rotation_y, transform.rotation_z, transform.rotation_w
        transform.rotation_x = cosine * x + sine * z
        transform.rotation_y = cosine * y + sine * w
        transform.rotation_z = cosine * z - sine * x
        transform.rotation_w = cosine * w - sine * y
        self.node.local_transform = transform
